using System.Text.RegularExpressions;
using MMDb.Application.Vault;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Vault;

/// <summary>
/// The vault as a folder of markdown files. Never overwrites, moves or deletes a file it didn't write: a note
/// is Shelf's only if its frontmatter says <c>managed_by: mmdb</c> with the film's id. Every write goes to a
/// temporary file first and is swapped in, and files whose content is unchanged are not touched at all.
/// </summary>
internal sealed partial class FileVaultWriter(string coverDirectory, TimeProvider clock) : IVaultWriter
{
    public const string MoviesFolder = "Movies";
    public const string CoversFolder = "Attachments/covers";
    private const string TrashFolder = ".trash";

    private readonly Lock _lock = new();

    // Each film's note file name in Movies/, for the folder it was read from.
    private string? _knownFolder;
    private Dictionary<Guid, string> _notes = [];

    public Task SyncAsync(string folder, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Scan(folder);
            var keep = movies.Select(m => m.Id).ToHashSet();
            foreach (var gone in notes.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                await RemoveNoteAsync(folder, notes, gone, cancellationToken);
            }

            // Oldest first, so the film that had a name first keeps it.
            foreach (var movie in movies.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id))
            {
                await WriteNoteAsync(folder, notes, movie, cancellationToken);
            }

            RemoveStrayCovers(folder, movies);
            await WriteSharedFilesAsync(folder, notes, movies, cancellationToken);
        });

    public Task WriteMovieAsync(string folder, Movie movie, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Known(folder, movie.Id);
            await WriteNoteAsync(folder, notes, movie, cancellationToken);
            await WriteSharedFilesAsync(folder, notes, movies, cancellationToken);
        });

    public Task RemoveMovieAsync(string folder, Guid movieId, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        GuardAsync(folder, async () =>
        {
            var notes = Known(folder, movieId);
            await RemoveNoteAsync(folder, notes, movieId, cancellationToken);
            await WriteSharedFilesAsync(folder, notes, movies, cancellationToken);
        });

    public string? NoteOf(string folder, Guid movieId)
    {
        lock (_lock)
        {
            return _knownFolder == folder && _notes.TryGetValue(movieId, out var name) ? $"{MoviesFolder}/{name}" : null;
        }
    }

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

    /// <summary>The notes known for this folder; read from disk if it's new, or if the film's note has gone missing.</summary>
    private Dictionary<Guid, string> Known(string folder, Guid movieId)
    {
        lock (_lock)
        {
            if (_knownFolder == folder && (!_notes.TryGetValue(movieId, out var name) || IsNoteOf(folder, name, movieId)))
            {
                return _notes;
            }
        }

        return Scan(folder);
    }

    private Dictionary<Guid, string> Scan(string folder)
    {
        var notes = new Dictionary<Guid, string>();
        var movies = Path.Combine(folder, MoviesFolder);
        if (Directory.Exists(movies))
        {
            foreach (var path in Directory.EnumerateFiles(movies, "*.md").Order(StringComparer.Ordinal))
            {
                // A copy of a note (same id twice) stays the user's.
                if (MovieNote.IdOf(ReadStart(path)) is { } id)
                {
                    notes.TryAdd(id, Path.GetFileName(path));
                }
            }
        }

        lock (_lock)
        {
            _knownFolder = folder;
            _notes = notes;
        }

        return notes;
    }

    private async Task WriteNoteAsync(string folder, Dictionary<Guid, string> notes, Movie movie, CancellationToken cancellationToken)
    {
        var movies = Path.Combine(folder, MoviesFolder);
        Directory.CreateDirectory(movies);

        notes.TryGetValue(movie.Id, out var old);
        var name = ChooseName(folder, notes, movie);
        var oldPath = old is null ? null : Path.Combine(movies, old);
        var path = Path.Combine(movies, name);

        var existing = oldPath is not null && File.Exists(oldPath) ? oldPath : File.Exists(path) ? path : null;
        var myNotes = existing is null ? "" : MovieNote.MyNotesOf(await File.ReadAllTextAsync(existing, cancellationToken));
        var cover = CopyCover(folder, movie);

        // A change of case only: rename the file itself (on Windows it is the same file).
        if (oldPath is not null && old != name && string.Equals(old, name, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Move(oldPath, path);
            oldPath = null;
        }

        await WriteFileAsync(path, MovieNote.Render(movie, cover, myNotes, clock.LocalTimeZone), cancellationToken);
        if (oldPath is not null && old != name && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        lock (_lock)
        {
            notes[movie.Id] = name;
        }
    }

    private static string ChooseName(string folder, Dictionary<Guid, string> notes, Movie movie)
    {
        var wanted = NoteNames.For(movie.Title, movie.Year);
        var current = notes.GetValueOrDefault(movie.Id);
        for (var n = 1; ; n++)
        {
            var name = NoteNames.Numbered(wanted, n) + ".md";
            if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            var takenByAnotherFilm = notes.Any(note => note.Key != movie.Id && string.Equals(note.Value, name, StringComparison.OrdinalIgnoreCase));
            var takenByAnotherFile = File.Exists(Path.Combine(folder, MoviesFolder, name)) && !IsNoteOf(folder, name, movie.Id);
            if (!takenByAnotherFilm && !takenByAnotherFile)
            {
                return name;
            }
        }
    }

    private async Task RemoveNoteAsync(string folder, Dictionary<Guid, string> notes, Guid movieId, CancellationToken cancellationToken)
    {
        if (notes.TryGetValue(movieId, out var name))
        {
            var path = Path.Combine(folder, MoviesFolder, name);
            if (IsNoteOf(folder, name, movieId))
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
                notes.Remove(movieId);
            }
        }

        RemoveCovers(folder, movieId, keep: null);
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

    /// <returns>The cover's path in the vault, or <see langword="null"/> if the film has none.</returns>
    private string? CopyCover(string folder, Movie movie)
    {
        var source = movie.CoverImage is { } image ? Path.Combine(coverDirectory, Path.GetFileName(image)) : null;
        if (source is null || !File.Exists(source))
        {
            RemoveCovers(folder, movie.Id, keep: null);
            return null;
        }

        var name = Path.GetFileName(source);
        var covers = Path.Combine(folder, CoversFolder);
        var target = Path.Combine(covers, name);

        // Named after the poster, so a cover that is there is the right one.
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(covers);
            var temp = TempPathFor(target);
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }

        RemoveCovers(folder, movie.Id, keep: name);
        return $"{CoversFolder}/{name}";
    }

    private static void RemoveCovers(string folder, Guid movieId, string? keep)
    {
        var covers = Path.Combine(folder, CoversFolder);
        if (!Directory.Exists(covers))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(covers, $"{movieId:N}-*"))
        {
            if (Path.GetFileName(path) != keep)
            {
                File.Delete(path);
            }
        }
    }

    // Covers whose film is gone. Only names Shelf gives covers (film id, dash) are considered.
    private static void RemoveStrayCovers(string folder, IReadOnlyList<Movie> movies)
    {
        var covers = Path.Combine(folder, CoversFolder);
        if (!Directory.Exists(covers))
        {
            return;
        }

        var ids = movies.Select(m => m.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(covers))
        {
            var cover = CoverName().Match(Path.GetFileName(path));
            if (cover.Success && !ids.Contains(cover.Groups["id"].Value))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task WriteSharedFilesAsync(string folder, Dictionary<Guid, string> notes, IReadOnlyList<Movie> movies, CancellationToken cancellationToken)
    {
        var indexed = movies
            .Where(m => notes.ContainsKey(m.Id))
            .Select(m => (m, Path.GetFileNameWithoutExtension(notes[m.Id])))
            .ToList();
        await WriteSharedFileAsync(folder, VaultFiles.Index, VaultFiles.RenderIndex(indexed), cancellationToken);
        await WriteSharedFileAsync(folder, VaultFiles.Guide, VaultFiles.GuideText, cancellationToken);
        await WriteSharedFileAsync(folder, VaultFiles.MoviesBase, VaultFiles.MoviesBaseText, cancellationToken);
    }

    private static async Task WriteSharedFileAsync(string folder, string name, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, name);
        if (File.Exists(path) && !VaultFiles.IsManaged(name, await File.ReadAllTextAsync(path, cancellationToken)))
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

    private static bool IsNoteOf(string folder, string name, Guid movieId)
    {
        var path = Path.Combine(folder, MoviesFolder, name);
        return File.Exists(path) && MovieNote.IdOf(ReadStart(path)) == movieId;
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
