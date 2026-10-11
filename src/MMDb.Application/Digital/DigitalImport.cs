using System.Collections.Concurrent;
using System.IO.Enumeration;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Digital;

/// <summary>What a search for a film to link a file to found.</summary>
public sealed record HitSearch(IReadOnlyList<MovieHit> Hits, MetadataProblem? Problem = null, string? FailedService = null);

/// <summary>
/// Use cases for digital films: scan the movie folder for video files, match them to TMDB by their names, add
/// the ones the user accepts, and keep track of files that were skipped, moved or deleted. What was found is
/// kept in <see cref="ImportDesk"/> between calls.
/// </summary>
/// <remarks>
/// A scan only reads the folder and the database. TMDB is asked when the import screen wants the matches
/// (<see cref="MatchAsync"/>), and a film's full details only when it is added.
/// </remarks>
public sealed class DigitalImport(
    ImportDesk desk,
    ISettingStore settings,
    ISkippedFileStore skips,
    IMovieRepository movies,
    IMovieMetadata metadata,
    MovieCatalog catalog,
    VaultMirror vault,
    VaultContentSource content,
    IFileOpener opener)
{
    /// <summary>At most this many films are kept as a file's possible matches.</summary>
    public const int MaxHits = 8;

    internal const string FolderSetting = "digital.folder";
    internal const string WatchSetting = "digital.watch";

    // TMDB allows far more at once; this keeps a few hundred files from looking like a flood.
    private const int LookupsAtOnce = 4;

    // From here on the vault is written once at the end instead of after every film.
    private const int BulkAdd = 10;

    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    /// <summary>Picks up the folder chosen last time and scans it. Called once when the PC app starts.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var folder = await settings.GetAsync(FolderSetting, cancellationToken);
        var watch = await settings.GetAsync(WatchSetting, cancellationToken) != "off";
        desk.Update(s => s with { Folder = folder, Watch = watch });
        await RescanAsync(cancellationToken);
    }

    /// <summary>Remembers the movie folder and scans it.</summary>
    /// <returns><see langword="false"/> (and nothing changed) if there is no such folder.</returns>
    public async Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        await settings.SetAsync(FolderSetting, folder, cancellationToken);

        // What was waiting in another folder has nothing to do with this one.
        desk.Update(s => Paths.Equals(s.Folder, folder) ? s : new() { Folder = folder, Watch = s.Watch });
        await RescanAsync(cancellationToken);
        return true;
    }

    /// <summary>Turns watching the movie folder on or off; turning it on also catches up on what arrived meanwhile.</summary>
    public async Task SetWatchAsync(bool on, CancellationToken cancellationToken = default)
    {
        await settings.SetAsync(WatchSetting, on ? null : "off", cancellationToken);
        Watch(desk.Update(s => s with { Watch = on }));
        await RescanAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the movie folder again: new files wait for review, files that are gone are flagged, and a film's
    /// file that turns up elsewhere in the folder under the same name is linked to it again.
    /// </summary>
    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        await desk.Turn.WaitAsync(cancellationToken);
        try
        {
            await ScanAsync(cancellationToken);
        }
        finally
        {
            desk.Turn.Release();
        }
    }

    /// <summary>Looks up on TMDB every file that hasn't been yet. The status shows how far it is.</summary>
    public async Task MatchAsync(CancellationToken cancellationToken = default)
    {
        if (!desk.TryStartMatching())
        {
            return;
        }

        var matched = new ConcurrentBag<string>();
        try
        {
            var waiting = desk.Status.Files.Where(f => f.State == FileMatchState.Waiting).ToList();
            if (waiting.Count == 0)
            {
                return;
            }

            desk.Update(s => s with { Problem = null, FailedService = null, Progress = new(ImportActivity.Matching, 0, waiting.Count) });
            await Parallel.ForEachAsync(
                waiting,
                new ParallelOptions { MaxDegreeOfParallelism = LookupsAtOnce, CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    var (name, hits) = await FindAsync(file.Parsed, token);
                    var (state, chosen) = Rate(name, hits);
                    matched.Add(file.Path);
                    desk.Update(s => s with
                    {
                        Files = Change(s.Files, file.Path, f => f with { State = state, Hits = hits, Chosen = chosen, Ticked = state == FileMatchState.Matched }),
                        Progress = s.Progress is { } progress ? progress with { Done = progress.Done + 1 } : null,
                    });
                });
        }
        catch (MetadataException ex)
        {
            desk.Update(s => s with { Problem = ex.Problem, FailedService = ex.Service });
        }
        finally
        {
            // Stopped before saying so: a screen that hears of it may ask for the files that arrived meanwhile.
            desk.StopMatching();
            desk.Update(s => s with { Progress = s.Progress is { Activity: ImportActivity.Matching } ? null : s.Progress });
        }

        await SettleAsync([.. matched], cancellationToken);
    }

    /// <summary>Searches TMDB for the film a file is, when the name didn't give it: a title, optionally ending in the year.</summary>
    public async Task<HitSearch> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var text = SearchText.Parse(query);
        if (text.Text.Length == 0)
        {
            return new([]);
        }

        try
        {
            return new((await FindAsync(new(text.Text, text.Title, text.Year), cancellationToken)).Hits);
        }
        catch (MetadataException ex)
        {
            return new([], ex.Problem, ex.Service);
        }
    }

    /// <summary>Says which film a waiting file is; it is ticked, unless that film already has a file.</summary>
    public async Task ChooseAsync(string path, MovieHit film, CancellationToken cancellationToken = default)
    {
        desk.Update(s => s with
        {
            Files = Change(s.Files, path, f => f with
            {
                Chosen = film,
                State = FileMatchState.Matched,
                Ticked = true,
                Hits = f.Hits.Contains(film) ? f.Hits : [film, .. f.Hits],
            }),
        });
        await SettleAsync([], cancellationToken);
    }

    /// <summary>Ticks or unticks a waiting file. Only a file with a film, whose film has no other file, can be ticked.</summary>
    public void Tick(string path, bool on) =>
        desk.Update(s => s with { Files = Change(s.Files, path, f => f with { Ticked = on && f.Chosen is not null && f.OwnedFile is null }) });

    /// <summary>Takes a waiting file off the list for good: later scans leave it out.</summary>
    public async Task SkipAsync(string path, CancellationToken cancellationToken = default)
    {
        if (desk.Status.Files.FirstOrDefault(f => Paths.Equals(f.Path, path)) is not { } file)
        {
            return;
        }

        await skips.AddAsync(file.Path, cancellationToken);
        desk.Update(s => s with
        {
            Files = [.. s.Files.Where(f => !Paths.Equals(f.Path, path))],
            Skipped = [.. s.Skipped.Where(p => !Paths.Equals(p, path)).Append(file.Path).Order(Paths)],
        });
        await SettleAsync([], cancellationToken);
    }

    /// <summary>Puts a skipped file back among the files waiting for review.</summary>
    public async Task BringBackAsync(string path, CancellationToken cancellationToken = default)
    {
        await skips.RemoveAsync(path, cancellationToken);
        desk.Update(s => s.Folder is { } folder && s.Skipped.FirstOrDefault(p => Paths.Equals(p, path)) is { } skipped
            ? s with
            {
                Skipped = [.. s.Skipped.Where(p => !Paths.Equals(p, path))],
                Files = [.. s.Files.Append(NewFile(folder, skipped)).OrderBy(f => f.Path, Paths)],
            }
            : s);
    }

    /// <summary>
    /// Adds every ticked file to the collection: a new film with what TMDB knows of it, or a digital copy on a
    /// film already owned. Files that could not be added stay on the list, unticked.
    /// </summary>
    /// <returns>How many were added.</returns>
    public async Task<int> AddTickedAsync(CancellationToken cancellationToken = default)
    {
        await desk.Turn.WaitAsync(cancellationToken);
        try
        {
            var ticked = desk.Status.Files.Where(f => f is { Ticked: true, Chosen: not null, OwnedFile: null }).ToList();
            if (ticked.Count == 0)
            {
                return 0;
            }

            desk.Update(s => s with { Problem = null, FailedService = null, Progress = new(ImportActivity.Adding, 0, ticked.Count) });
            var held = ticked.Count >= BulkAdd;
            if (held)
            {
                vault.Hold();
            }

            var added = 0;
            try
            {
                foreach (var files in ticked.Chunk(LookupsAtOnce))
                {
                    // A film already owned keeps its details; the others are fetched a few at a time.
                    var details = await Task.WhenAll(files.Select(f =>
                        f.Owned is null ? metadata.GetAsync(f.Chosen!.TmdbId, cancellationToken) : Task.FromResult<MovieDetails?>(null)));
                    foreach (var (file, film) in files.Zip(details))
                    {
                        var saved = await AddAsync(file, film, cancellationToken);
                        added += saved ? 1 : 0;
                        desk.Update(s => s with
                        {
                            Files = saved
                                ? [.. s.Files.Where(f => !Paths.Equals(f.Path, file.Path))]
                                : Change(s.Files, file.Path, f => f with { Ticked = false, State = FileMatchState.Check }),
                            LinkedCount = s.LinkedCount + (saved ? 1 : 0),
                            LinkedBytes = s.LinkedBytes + (saved ? SizeOf(file.Path) : 0),
                            Progress = s.Progress is { } progress ? progress with { Done = progress.Done + 1 } : null,
                        });
                    }
                }
            }
            catch (MetadataException ex)
            {
                desk.Update(s => s with { Problem = ex.Problem, FailedService = ex.Service });
            }
            finally
            {
                desk.Update(s => s with { Progress = null });
                if (held)
                {
                    await vault.ReleaseAsync(content);
                }
            }

            await SettleAsync([], cancellationToken);
            return added;
        }
        finally
        {
            desk.Turn.Release();
        }
    }

    /// <summary>
    /// For a waiting file whose film already has a file: makes this one the film's file. The file it had is
    /// skipped from then on, so it doesn't come back asking the same.
    /// </summary>
    /// <returns><see langword="false"/> if the file isn't waiting, or its film isn't in the collection (any more).</returns>
    public async Task<bool> UseInsteadAsync(string path, CancellationToken cancellationToken = default)
    {
        await desk.Turn.WaitAsync(cancellationToken);
        try
        {
            var status = desk.Status;
            if (status.Files.FirstOrDefault(f => Paths.Equals(f.Path, path)) is not { Owned: { } film } file
                || await catalog.LinkFileAsync(film.Id, file.Path, replace: true, cancellationToken) is not SaveMovieResult.Saved)
            {
                return false;
            }

            if (file.OwnedFile is { } replaced && status.Folder is { } folder && IsIn(folder, FullPath(replaced)))
            {
                await skips.AddAsync(FullPath(replaced), cancellationToken);
            }

            await ScanAsync(cancellationToken);
            return true;
        }
        finally
        {
            desk.Turn.Release();
        }
    }

    /// <summary>Says where a film's video file is now, for a film whose file is missing.</summary>
    /// <returns><see langword="false"/> if it isn't a video file that exists, or the film isn't in the collection.</returns>
    public async Task<bool> LocateAsync(Guid movieId, string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path) || !ReleaseNameParser.IsVideoFile(path) || !File.Exists(path))
        {
            return false;
        }

        await desk.Turn.WaitAsync(cancellationToken);
        try
        {
            if (await catalog.LinkFileAsync(movieId, Path.GetFullPath(path), replace: true, cancellationToken) is not SaveMovieResult.Saved)
            {
                return false;
            }

            await ScanAsync(cancellationToken);
            return true;
        }
        finally
        {
            desk.Turn.Release();
        }
    }

    /// <summary>
    /// Removes a film's digital copy (its file is gone and isn't coming back). A film owned on nothing else
    /// leaves the collection.
    /// </summary>
    /// <returns><see langword="false"/> if the film isn't in the collection or has no digital copy.</returns>
    public async Task<bool> RemoveCopyAsync(Guid movieId, CancellationToken cancellationToken = default)
    {
        await desk.Turn.WaitAsync(cancellationToken);
        try
        {
            if (await catalog.GetAsync(movieId, cancellationToken) is not { } movie || !movie.Owns(MediaFormat.Digital))
            {
                return false;
            }

            var removed = movie.Formats.Count == 1
                ? await catalog.DeleteAsync(movieId, cancellationToken)
                : await catalog.RemoveFormatAsync(movieId, MediaFormat.Digital, cancellationToken) is SaveMovieResult.Saved;
            if (removed)
            {
                await ScanAsync(cancellationToken);
            }

            return removed;
        }
        finally
        {
            desk.Turn.Release();
        }
    }

    /// <summary>Opens a film's video file in the PC's player.</summary>
    /// <returns><see langword="false"/> if the film has no file, the file isn't there, or the PC could not open it.</returns>
    public async Task<bool> PlayAsync(Guid movieId, CancellationToken cancellationToken = default)
    {
        var path = (await catalog.GetAsync(movieId, cancellationToken))?.Formats.FirstOrDefault(f => f.Format == MediaFormat.Digital)?.FilePath;

        // Video files only: a path can be typed by hand or come from a phone, and opening runs whatever it names.
        return path is not null && ReleaseNameParser.IsVideoFile(path) && File.Exists(path) && opener.Open(path);
    }

    /// <summary>
    /// A file is matched when one film has the name's title and year (a year either side will do when no film has
    /// the year itself: TMDB dates some films by a festival), or when TMDB's best match is one of several that do.
    /// </summary>
    internal static (FileMatchState State, MovieHit? Chosen) Rate(ReleaseName name, IReadOnlyList<MovieHit> hits)
    {
        if (hits.Count == 0)
        {
            return (FileMatchState.NoMatch, null);
        }

        if (name.Year is { } year)
        {
            var titled = hits.Where(h => (name.IsTitled(h.Title) || name.IsTitled(h.OriginalTitle)) && h.Year is { } y && Math.Abs(y - year) <= 1).ToList();
            var thatYear = titled.Where(h => h.Year == year).ToList();

            // TMDB has shorts and student films sharing a title and a year with a well-known film ("Arrival",
            // 2016): of several, the one TMDB itself puts first is the film meant.
            if (thatYear.Count == 1 || (thatYear.Count > 1 && thatYear[0] == hits[0]))
            {
                return (FileMatchState.Matched, thatYear[0]);
            }

            if (thatYear.Count == 0 && titled.Count == 1)
            {
                return (FileMatchState.Matched, titled[0]);
            }
        }

        return (FileMatchState.Check, hits[0]);
    }

    // Must be called in turn (see ImportDesk.Turn).
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (desk.Status.Folder is not { } folder)
        {
            Watch(desk.Update(s => new() { Watch = s.Watch }));
            return;
        }

        if (VideoFiles(folder) is not { } files)
        {
            // Its drive may only be unplugged: nothing is called missing, and what was waiting is forgotten.
            Watch(desk.Update(s => new() { State = ImportFolderState.FolderMissing, Folder = folder, Watch = s.Watch }));
            return;
        }

        var found = files.ConvertAll(f => f.Path);
        var copies = await movies.ListDigitalAsync(cancellationToken);
        var linked = copies.Select(c => c.FilePath).OfType<string>().Select(FullPath).ToHashSet(Paths);
        var loose = found.Where(f => !linked.Contains(f)).ToList();
        var here = found.ToHashSet(Paths);
        var gone = copies.Where(c => c.FilePath is { } path && IsIn(folder, FullPath(path)) && !here.Contains(FullPath(path))).ToList();
        if (await RelinkAsync(gone, loose, cancellationToken))
        {
            copies = await movies.ListDigitalAsync(cancellationToken);
        }

        var skipped = (await skips.ListAsync(cancellationToken)).ToHashSet(Paths);
        var owned = await OwnedAsync(cancellationToken);
        var unlinked = loose.ToHashSet(Paths);
        Watch(desk.Update(s => s with
        {
            State = ImportFolderState.Ready,
            VideoFileCount = found.Count,
            LinkedCount = found.Count - loose.Count,
            LinkedBytes = files.Where(f => !unlinked.Contains(f.Path)).Sum(f => f.Size),
            Files = Settle(Merge(s.Files, loose.Where(f => !skipped.Contains(f)), folder), owned, copies, []),
            Skipped = [.. loose.Where(skipped.Contains)],
            Missing = [.. gone.Select(c => new MissingFile(c.MovieId, c.Title, c.Year, c.FilePath!, c.Formats.Count == 1, c.CoverImage))],
        }));
    }

    // A file moved inside the folder keeps its name: when exactly one unlinked file has the name of exactly one
    // missing file, it is that film's. Both lists lose what was linked again.
    private async Task<bool> RelinkAsync(List<DigitalCopy> gone, List<string> loose, CancellationToken cancellationToken)
    {
        if (gone.Count == 0 || loose.Count == 0)
        {
            return false;
        }

        var looseByName = loose.ToLookup(path => Path.GetFileName(path), Paths);
        var goneByName = gone.ToLookup(c => Path.GetFileName(c.FilePath!), Paths);
        var relinked = false;
        foreach (var copy in gone.ToList())
        {
            var name = Path.GetFileName(copy.FilePath!);
            if (goneByName[name].Count() == 1 && looseByName[name].Take(2).ToList() is [var moved]
                && await catalog.LinkFileAsync(copy.MovieId, moved, replace: true, cancellationToken) is SaveMovieResult.Saved)
            {
                gone.Remove(copy);
                loose.Remove(moved);
                relinked = true;
            }
        }

        return relinked;
    }

    private async Task<bool> AddAsync(ImportFile file, MovieDetails? film, CancellationToken cancellationToken)
    {
        if (file.Owned is { } owned)
        {
            return await catalog.LinkFileAsync(owned.Id, file.Path, replace: false, cancellationToken) is SaveMovieResult.Saved;
        }

        // A film added a moment ago for another file comes back as already owned: this file stays on the list.
        return film is not null
            && await catalog.AddAsync(film, [new(MediaFormat.Digital, FilePath: file.Path)], cancellationToken) is SaveMovieResult.Saved;
    }

    // A number that looks like a year may belong to the title ("Blade Runner 2049"), so no hits with it means try without.
    private async Task<(ReleaseName Name, IReadOnlyList<MovieHit> Hits)> FindAsync(ReleaseName name, CancellationToken cancellationToken)
    {
        var hits = await metadata.FindAsync(name.Title, name.Year, cancellationToken);
        if (hits.Count == 0 && name.Year is not null)
        {
            name = new(name.Text, name.Text, null);
            hits = await metadata.FindAsync(name.Title, null, cancellationToken);
        }

        return (name, [.. hits.Take(MaxHits)]);
    }

    private async Task SettleAsync(IReadOnlyCollection<string> matched, CancellationToken cancellationToken)
    {
        var copies = await movies.ListDigitalAsync(cancellationToken);
        var owned = await OwnedAsync(cancellationToken);
        desk.Update(s => s with { Files = Settle(s.Files, owned, copies, matched) });
    }

    // The films in the collection that the waiting files were matched to, by TMDB id.
    private async Task<Dictionary<int, MovieListing>> OwnedAsync(CancellationToken cancellationToken)
    {
        var ids = desk.Status.Files.Select(f => f.Chosen?.TmdbId).OfType<int>().Distinct().ToList();
        return ids.Count == 0
            ? []
            : (await movies.ListByTmdbIdsAsync(ids, cancellationToken)).GroupBy(m => m.TmdbId!.Value).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>
    /// Says of each waiting file what the collection has of its film. A file whose film already has another
    /// file can't be ticked.
    /// </summary>
    /// <param name="matched">
    /// Files that were just looked up. Of two that turned out to be the same film, the later one is unticked
    /// for the user to look at instead of being added as a second file.
    /// </param>
    private static List<ImportFile> Settle(
        IEnumerable<ImportFile> files, Dictionary<int, MovieListing> owned, IReadOnlyList<DigitalCopy> copies, IReadOnlyCollection<string> matched)
    {
        var justMatched = matched.ToHashSet(Paths);
        var fileOf = copies.Where(c => c.FilePath is not null).ToDictionary(c => c.MovieId, c => c.FilePath!);
        var firstFor = new Dictionary<int, string>();
        var settled = new List<ImportFile>();
        foreach (var file in files)
        {
            if (file.Chosen is not { } film)
            {
                settled.Add(file with { Owned = null, OwnedFile = null, SameFilmAs = null });
                continue;
            }

            var had = owned.GetValueOrDefault(film.TmdbId);
            var other = had is not null && fileOf.GetValueOrDefault(had.Id) is { } path && !Paths.Equals(FullPath(path), file.Path) ? path : null;

            // Only files that are surely the film count: a guess at the same film says nothing yet.
            var twin = file.State != FileMatchState.Matched || firstFor.TryAdd(film.TmdbId, file.Name) ? null : firstFor[film.TmdbId];
            settled.Add(file with
            {
                Owned = had,
                OwnedFile = other,
                SameFilmAs = twin,
                Ticked = file.Ticked && other is null && !(twin is not null && justMatched.Contains(file.Path)),
            });
        }

        return settled;
    }

    // Files known from before keep what was found out about them.
    private static IEnumerable<ImportFile> Merge(IReadOnlyList<ImportFile> known, IEnumerable<string> waiting, string folder)
    {
        var rows = known.ToDictionary(f => f.Path, Paths);
        return waiting.Select(path => rows.GetValueOrDefault(path) ?? NewFile(folder, path));
    }

    private static ImportFile NewFile(string folder, string path)
    {
        var name = path[Prefix(folder).Length..];
        return new(path, name, ReleaseNameParser.ParsePath(name));
    }

    private static IReadOnlyList<ImportFile> Change(IReadOnlyList<ImportFile> files, string path, Func<ImportFile, ImportFile> change) =>
        [.. files.Select(f => Paths.Equals(f.Path, path) ? change(f) : f)];

    private void Watch(ImportStatus status) =>
        desk.Watch(status is { Watch: true, State: ImportFolderState.Ready } ? status.Folder : null);

    /// <returns>
    /// Every video file in the folder and under it, by path, with its size (which listing a folder gives for
    /// free); <see langword="null"/> if the folder can't be read.
    /// </returns>
    private static List<(string Path, long Size)>? VideoFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            var everywhere = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var entries = new FileSystemEnumerable<(string Path, long Size)>(
                folder, (ref FileSystemEntry entry) => (entry.ToSpecifiedFullPath(), entry.Length), everywhere)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            };
            return [.. entries.Where(f => ReleaseNameParser.IsVideoFile(f.Path)).OrderBy(f => f.Path, Paths)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string Prefix(string folder) =>
        Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

    private static bool IsIn(string folder, string path) => path.StartsWith(Prefix(folder), StringComparison.OrdinalIgnoreCase);

    // A path as the folder scan spells it; one that can't be made sense of is left as typed.
    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
