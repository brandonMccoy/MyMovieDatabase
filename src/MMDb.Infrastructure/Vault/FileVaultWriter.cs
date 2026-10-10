using System.Text.RegularExpressions;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Vault;

/// <summary>
/// The vault as a folder of markdown files. Never overwrites, moves or deletes a file it didn't write: a note
/// is Shelf's only if its frontmatter says <c>managed_by: mmdb</c> with the film's or comic's id. Every write goes
/// to a temporary file first and is swapped in, and files whose content is unchanged are not touched at all.
/// </summary>
internal sealed partial class FileVaultWriter(string coverDirectory, TimeProvider clock) : IVaultWriter
{
    public const string MoviesFolder = "Movies";
    public const string ComicsFolder = "Comics";
    public const string CollectedFolder = "Comics/Trades & HCs";
    public const string CoversFolder = "Attachments/covers";
    private const string TrashFolder = ".trash";

    private readonly Lock _lock = new();

    // Each note's path in the vault ("Movies/Heat (1995).md"), for the folder it was read from.
    private string? _knownFolder;
    private Dictionary<Guid, string> _notes = [];

    public Task SyncAsync(string folder, VaultContent content, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Scan(folder);
            var keep = content.Movies.Select(m => m.Id).Concat(content.Comics.Select(c => c.Id)).ToHashSet();
            foreach (var gone in notes.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                await RemoveNoteAsync(folder, notes, gone, cancellationToken);
            }

            // Oldest first, so whatever had a name first keeps it.
            foreach (var movie in content.Movies.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id))
            {
                await WriteMovieNoteAsync(folder, notes, movie, cancellationToken);
            }

            foreach (var comic in content.Comics.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            {
                await WriteComicNoteAsync(folder, notes, comic, cancellationToken);
            }

            RemoveStrayCovers(folder, keep);
            await WriteSharedFilesAsync(folder, notes, content, cancellationToken);
        });

    public Task WriteMovieAsync(string folder, Movie movie, VaultContent content, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Known(folder, movie.Id);
            await WriteMovieNoteAsync(folder, notes, movie, cancellationToken);
            await WriteSharedFilesAsync(folder, notes, content, cancellationToken);
        });

    public Task WriteComicAsync(string folder, Comic comic, VaultContent content, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Known(folder, comic.Id);
            await WriteComicNoteAsync(folder, notes, comic, cancellationToken);
            await WriteSharedFilesAsync(folder, notes, content, cancellationToken);
        });

    public Task RemoveAsync(string folder, Guid id, VaultContent content, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Known(folder, id);
            await RemoveNoteAsync(folder, notes, id, cancellationToken);
            await WriteSharedFilesAsync(folder, notes, content, cancellationToken);
        });

    public string? NoteOf(string folder, Guid id)
    {
        lock (_lock)
        {
            return _knownFolder == folder && _notes.TryGetValue(id, out var note) ? note : null;
        }
    }

    /// <summary>The folder a single issue's note goes in: <c>Comics/Saga (2012)</c>.</summary>
    public static string SeriesFolderOf(Comic comic) => $"{ComicsFolder}/{NoteNames.For(comic.Series ?? "", comic.SeriesYear)}";

    private static async Task GuardAsync(string folder, Func<Task> write)
    {
        if (!Directory.Exists(folder))
        {
            throw new VaultUnavailableException(VaultProblem.FolderMissing, $"Folder not found: {folder}");
        }

        try
        {
            await write();
        }
        catch (DirectoryNotFoundException e) when (!Directory.Exists(folder))
        {
            throw new VaultUnavailableException(VaultProblem.FolderMissing, $"Folder not found: {folder}", e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new VaultUnavailableException(VaultProblem.CannotWrite, e.Message, e);
        }
    }

    /// <summary>The notes known for this folder; read from disk if it's new, or if this note has gone missing.</summary>
    private Dictionary<Guid, string> Known(string folder, Guid id)
    {
        lock (_lock)
        {
            if (_knownFolder == folder && (!_notes.TryGetValue(id, out var note) || IsNoteOf(folder, note, id)))
            {
                return _notes;
            }
        }

        return Scan(folder);
    }

    private Dictionary<Guid, string> Scan(string folder)
    {
        var notes = new Dictionary<Guid, string>();
        AddNotes(folder, notes, MoviesFolder, SearchOption.TopDirectoryOnly);
        AddNotes(folder, notes, ComicsFolder, SearchOption.AllDirectories);

        lock (_lock)
        {
            _knownFolder = folder;
            _notes = notes;
        }

        return notes;
    }

    private static void AddNotes(string folder, Dictionary<Guid, string> notes, string notesFolder, SearchOption depth)
    {
        var root = Path.Combine(folder, notesFolder);
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.md", depth).Order(StringComparer.Ordinal))
        {
            // A copy of a note (same id twice) stays the user's.
            if (MovieNote.IdOf(ReadStart(path)) is { } id)
            {
                notes.TryAdd(id, Path.GetRelativePath(folder, path).Replace('\\', '/'));
            }
        }
    }

    private Task WriteMovieNoteAsync(string folder, Dictionary<Guid, string> notes, Movie movie, CancellationToken cancellationToken) =>
        WriteNoteAsync(folder, notes, movie, MoviesFolder, NoteNames.For(movie.Title, movie.Year),
            (cover, myNotes) => MovieNote.Render(movie, cover, myNotes, clock.LocalTimeZone), cancellationToken);

    private Task WriteComicNoteAsync(string folder, Dictionary<Guid, string> notes, Comic comic, CancellationToken cancellationToken)
    {
        if (comic.Format.IsCollected())
        {
            return WriteNoteAsync(folder, notes, comic, CollectedFolder, NoteNames.Clean(comic.DisplayName),
                (cover, myNotes) => ComicNote.Render(comic, cover, null, myNotes, clock.LocalTimeZone), cancellationToken);
        }

        var seriesFolder = SeriesFolderOf(comic);
        var series = ($"{seriesFolder}/{Path.GetFileNameWithoutExtension(ComicNote.SeriesNoteName)}", SeriesTitle(comic));
        return WriteNoteAsync(folder, notes, comic, seriesFolder, NoteNames.ForIssue(comic.Series!, comic.Number!, comic.Variant, comic.Printing),
            (cover, myNotes) => ComicNote.Render(comic, cover, series, myNotes, clock.LocalTimeZone), cancellationToken);
    }

    private static string SeriesTitle(Comic comic) => comic.SeriesYear is { } year ? $"{comic.Series} ({year})" : comic.Series!;

    /// <param name="notesFolder">Where the note belongs, relative to the vault: <c>Movies</c>, <c>Comics/Saga (2012)</c>.</param>
    /// <param name="wanted">The note's name without extension, before any " (2)".</param>
    /// <param name="render">The note's text, given its cover in the vault and the My notes to keep.</param>
    private async Task WriteNoteAsync(
        string folder,
        Dictionary<Guid, string> notes,
        ProductBase product,
        string notesFolder,
        string wanted,
        Func<string?, string, string> render,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(folder, notesFolder));

        notes.TryGetValue(product.Id, out var old);
        var note = ChooseName(folder, notes, product.Id, notesFolder, wanted);
        var oldPath = old is null ? null : Path.Combine(folder, old);
        var path = Path.Combine(folder, note);

        var existing = oldPath is not null && File.Exists(oldPath) ? oldPath : File.Exists(path) ? path : null;
        var myNotes = existing is null ? "" : MovieNote.MyNotesOf(await File.ReadAllTextAsync(existing, cancellationToken));
        var cover = CopyCover(folder, product);

        // A change of case only: rename the file itself (on Windows it is the same file).
        if (oldPath is not null && old != note && string.Equals(old, note, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Move(oldPath, path);
            oldPath = null;
        }

        await WriteFileAsync(path, render(cover, myNotes), cancellationToken);
        if (oldPath is not null && old != note && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        lock (_lock)
        {
            notes[product.Id] = note;
        }
    }

    private static string ChooseName(string folder, Dictionary<Guid, string> notes, Guid id, string notesFolder, string wanted)
    {
        var current = notes.GetValueOrDefault(id);
        for (var n = 1; ; n++)
        {
            var note = $"{notesFolder}/{NoteNames.Numbered(wanted, n)}.md";
            if (string.Equals(note, current, StringComparison.OrdinalIgnoreCase))
            {
                return note;
            }

            var takenByAnotherNote = notes.Any(other => other.Key != id && string.Equals(other.Value, note, StringComparison.OrdinalIgnoreCase));
            var takenByAnotherFile = (File.Exists(Path.Combine(folder, note)) && !IsNoteOf(folder, note, id))
                || string.Equals(Path.GetFileName(note), ComicNote.SeriesNoteName, StringComparison.OrdinalIgnoreCase);
            if (!takenByAnotherNote && !takenByAnotherFile)
            {
                return note;
            }
        }
    }

    private async Task RemoveNoteAsync(string folder, Dictionary<Guid, string> notes, Guid id, CancellationToken cancellationToken)
    {
        if (notes.TryGetValue(id, out var note))
        {
            var path = Path.Combine(folder, note);
            if (IsNoteOf(folder, note, id))
            {
                if (MovieNote.HasMyNotes(await File.ReadAllTextAsync(path, cancellationToken)))
                {
                    MoveToTrash(folder, path);
                }
                else
                {
                    File.Delete(path);
                }
            }

            lock (_lock)
            {
                notes.Remove(id);
            }
        }

        RemoveCovers(folder, id, keep: null);
    }

    private static void MoveToTrash(string folder, string path)
    {
        var trash = Path.Combine(folder, TrashFolder);
        Directory.CreateDirectory(trash);
        var name = Path.GetFileNameWithoutExtension(path);
        for (var n = 1; ; n++)
        {
            var target = Path.Combine(trash, NoteNames.Numbered(name, n) + ".md");
            if (!File.Exists(target))
            {
                File.Move(path, target);
                return;
            }
        }
    }

    /// <returns>The cover's path in the vault, or <see langword="null"/> if there is none.</returns>
    private string? CopyCover(string folder, ProductBase product)
    {
        var source = product.CoverImage is { } image ? Path.Combine(coverDirectory, Path.GetFileName(image)) : null;
        if (source is null || !File.Exists(source))
        {
            RemoveCovers(folder, product.Id, keep: null);
            return null;
        }

        var name = Path.GetFileName(source);
        var covers = Path.Combine(folder, CoversFolder);
        var target = Path.Combine(covers, name);

        // Named after the image, so a cover that is there is the right one.
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(covers);
            var temp = TempPathFor(target);
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }

        RemoveCovers(folder, product.Id, keep: name);
        return $"{CoversFolder}/{name}";
    }

    private static void RemoveCovers(string folder, Guid id, string? keep)
    {
        var covers = Path.Combine(folder, CoversFolder);
        if (!Directory.Exists(covers))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(covers, $"{id:N}-*"))
        {
            if (Path.GetFileName(path) != keep)
            {
                File.Delete(path);
            }
        }
    }

    // Covers whose film or comic is gone. Only names Shelf gives covers (id, dash) are considered.
    private static void RemoveStrayCovers(string folder, HashSet<Guid> owned)
    {
        var covers = Path.Combine(folder, CoversFolder);
        if (!Directory.Exists(covers))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(covers))
        {
            var cover = CoverName().Match(Path.GetFileName(path));
            if (cover.Success && !owned.Contains(Guid.ParseExact(cover.Groups["id"].Value, "N")))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task WriteSharedFilesAsync(string folder, Dictionary<Guid, string> notes, VaultContent content, CancellationToken cancellationToken)
    {
        static string WithoutExtension(string note) => note[..^".md".Length];

        var movies = content.Movies
            .Where(m => notes.ContainsKey(m.Id))
            .Select(m => (m, Path.GetFileNameWithoutExtension(notes[m.Id])))
            .ToList();
        var collected = content.Comics
            .Where(c => c.Format.IsCollected() && notes.ContainsKey(c.Id))
            .Select(c => (c, WithoutExtension(notes[c.Id])))
            .ToList();

        var facts = content.Series.ToDictionary(s => s.MetronId);
        var runs = content.Comics
            .Where(c => !c.Format.IsCollected() && notes.ContainsKey(c.Id))
            .GroupBy(c => Path.GetDirectoryName(notes[c.Id])!.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(run =>
            {
                var copies = run.ToList();
                var seriesId = copies.Select(c => c.MetronSeriesId).OfType<int>().GroupBy(id => id).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
                var title = copies.GroupBy(SeriesTitle, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
                return (Folder: run.Key, Entry: new SeriesIndexEntry(title, $"{run.Key}/{Path.GetFileNameWithoutExtension(ComicNote.SeriesNoteName)}", copies,
                    seriesId is { } id ? facts.GetValueOrDefault(id) : null));
            })
            .ToList();

        foreach (var (seriesFolder, entry) in runs)
        {
            var issues = entry.Copies.Select(c => (c, WithoutExtension(notes[c.Id]))).ToList();
            await WriteSharedFileAsync(folder, $"{seriesFolder}/{ComicNote.SeriesNoteName}", ComicNote.RenderSeries(entry.Title, issues, entry.Facts), cancellationToken);
        }

        RemoveEmptiedSeries(folder, runs.Select(r => r.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase));

        await WriteSharedFileAsync(folder, VaultFiles.Index, VaultFiles.RenderIndex(movies, [.. runs.Select(r => r.Entry)], collected), cancellationToken);
        await WriteSharedFileAsync(folder, VaultFiles.Guide, VaultFiles.GuideText, cancellationToken);
        await WriteSharedFileAsync(folder, VaultFiles.MoviesBase, VaultFiles.MoviesBaseText, cancellationToken);
        await WriteSharedFileAsync(folder, VaultFiles.ComicsBase, VaultFiles.ComicsBaseText, cancellationToken);
    }

    // A series folder with no issues left loses its series note, and the folder too once nothing else is in it.
    private static void RemoveEmptiedSeries(string folder, HashSet<string> inUse)
    {
        var comics = Path.Combine(folder, ComicsFolder);
        if (!Directory.Exists(comics))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(comics))
        {
            var relative = Path.GetRelativePath(folder, directory).Replace('\\', '/');
            if (inUse.Contains(relative))
            {
                continue;
            }

            var seriesNote = Path.Combine(directory, ComicNote.SeriesNoteName);
            var removed = false;
            if (File.Exists(seriesNote) && VaultFiles.IsManaged(ComicNote.SeriesNoteName, File.ReadAllText(seriesNote)))
            {
                File.Delete(seriesNote);
                removed = true;
            }

            // Only folders Shelf made: a folder of the user's own stays, empty or not.
            if ((removed || relative == CollectedFolder) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static async Task WriteSharedFileAsync(string folder, string name, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, name);
        if (File.Exists(path) && !VaultFiles.IsManaged(Path.GetFileName(name), await File.ReadAllTextAsync(path, cancellationToken)))
        {
            return;
        }

        await WriteFileAsync(path, text, cancellationToken);
    }

    private static async Task WriteFileAsync(string path, string text, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == text)
        {
            return;
        }

        var temp = TempPathFor(path);
        try
        {
            await File.WriteAllTextAsync(temp, text, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // Hidden (leading dot) and not .md, so Obsidian never lists it.
    private static string TempPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.shelf-tmp");

    private static bool IsNoteOf(string folder, string note, Guid id)
    {
        var path = Path.Combine(folder, note);
        return File.Exists(path) && MovieNote.IdOf(ReadStart(path)) == id;
    }

    // Enough of a note to read its frontmatter.
    private static string ReadStart(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[4096];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    [GeneratedRegex("^(?<id>[0-9a-f]{32})-", RegexOptions.IgnoreCase)]
    private static partial Regex CoverName();
}
