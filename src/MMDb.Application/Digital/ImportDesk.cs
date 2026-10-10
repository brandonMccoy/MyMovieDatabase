using MMDb.Application.Metadata;
using MMDb.Domain.Movies;

namespace MMDb.Application.Digital;

public enum ImportFolderState
{
    /// <summary>No movie folder has been chosen.</summary>
    NotSetUp,

    Ready,

    /// <summary>The movie folder isn't there (renamed, or on a drive that isn't connected). Nothing is flagged as missing.</summary>
    FolderMissing,
}

public enum FileMatchState
{
    /// <summary>Not looked up yet.</summary>
    Waiting,

    /// <summary>One film fits the name and the year, or the user picked the film.</summary>
    Matched,

    /// <summary>A guess that needs a look: several films fit, the name has no year, or the title is a little off.</summary>
    Check,

    NoMatch,
}

/// <summary>A video file in the movie folder that is waiting for review: not linked to a film and not skipped.</summary>
/// <param name="Name">The path inside the movie folder, as the list shows it.</param>
/// <param name="Parsed">What the name says the film is.</param>
public sealed record ImportFile(string Path, string Name, ReleaseName Parsed)
{
    public FileMatchState State { get; init; } = FileMatchState.Waiting;

    /// <summary>What the search found, best first.</summary>
    public IReadOnlyList<MovieHit> Hits { get; init; } = [];

    /// <summary>The film the file would be added as.</summary>
    public MovieHit? Chosen { get; init; }

    /// <summary>Whether "Add" takes this file.</summary>
    public bool Ticked { get; init; }

    /// <summary>The film in the collection that <see cref="Chosen"/> is, if it is owned: the file is added to it.</summary>
    public MovieListing? Owned { get; init; }

    /// <summary>The other video file <see cref="Owned"/> already has. Such a file is never ticked; it can replace that one or be skipped.</summary>
    public string? OwnedFile { get; init; }

    /// <summary>The <see cref="Name"/> of a file earlier in the list that is matched to the same film.</summary>
    public string? SameFilmAs { get; init; }
}

/// <summary>A film whose video file is no longer where the collection says, inside the movie folder.</summary>
/// <param name="OnlyCopy">Whether the film is owned on nothing but Digital: removing the copy removes the film.</param>
public sealed record MissingFile(Guid MovieId, string Title, int? Year, string Path, bool OnlyCopy, string? CoverImage = null);

public enum ImportActivity
{
    /// <summary>Looking files up on TMDB.</summary>
    Matching,

    /// <summary>Adding the ticked files to the collection.</summary>
    Adding,
}

public sealed record ImportProgress(ImportActivity Activity, int Done, int Total);

/// <summary>The movie folder and what is in it, as of the last scan.</summary>
public sealed record ImportStatus
{
    public ImportFolderState State { get; init; } = ImportFolderState.NotSetUp;

    public string? Folder { get; init; }

    /// <summary>Whether new files in the folder show up for review by themselves.</summary>
    public bool Watch { get; init; } = true;

    /// <summary>Video files in the folder, whatever has become of them.</summary>
    public int VideoFileCount { get; init; }

    /// <summary>Of those, the ones that are a film's digital copy already.</summary>
    public int LinkedCount { get; init; }

    /// <summary>Waiting for review, by <see cref="ImportFile.Name"/>.</summary>
    public IReadOnlyList<ImportFile> Files { get; init; } = [];

    /// <summary>Skipped files that are in the folder, by path.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    public IReadOnlyList<MissingFile> Missing { get; init; } = [];

    /// <summary>Set while files are being looked up or added.</summary>
    public ImportProgress? Progress { get; init; }

    /// <summary>Why the last lookup stopped early, if it did.</summary>
    public MetadataProblem? Problem { get; init; }

    public string? FailedService { get; init; }
}

/// <summary>
/// Holds what the PC knows about the movie folder between scans, and watches it. One per app; the use cases
/// are in <see cref="DigitalImport"/>.
/// </summary>
public sealed class ImportDesk(IFolderWatcher watcher) : IDisposable
{
    private readonly Lock _lock = new();
    private ImportStatus _status = new();
    private IDisposable? _watching;
    private string? _watched;
    private int _matching;

    /// <summary>The status changed; raised on any thread.</summary>
    public event Action? Changed;

    /// <summary>Files arrived in, left or moved inside the watched folder: time to scan it again. Raised on any thread.</summary>
    public event Action? FolderChanged;

    public ImportStatus Status => Volatile.Read(ref _status);

    /// <summary>Scans and saves take turns, so each works from what the one before left.</summary>
    internal SemaphoreSlim Turn { get; } = new(1, 1);

    internal ImportStatus Update(Func<ImportStatus, ImportStatus> change)
    {
        ImportStatus status;
        lock (_lock)
        {
            status = change(_status);
            Volatile.Write(ref _status, status);
        }

        Changed?.Invoke();
        return status;
    }

    /// <summary>Watches <paramref name="folder"/> instead of what was watched; <see langword="null"/> stops watching.</summary>
    internal void Watch(string? folder)
    {
        lock (_lock)
        {
            if (string.Equals(folder, _watched, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _watching?.Dispose();
            _watching = null;
            _watched = null;
            if (folder is null)
            {
                return;
            }

            try
            {
                _watching = watcher.Watch(folder, () => FolderChanged?.Invoke());
                _watched = folder;
            }
            catch (IOException)
            {
                // Not watched: the next scan (at startup, or asked for) still finds what changed.
            }
        }
    }

    /// <returns><see langword="false"/> if files are being looked up already.</returns>
    internal bool TryStartMatching() => Interlocked.CompareExchange(ref _matching, 1, 0) == 0;

    internal void StopMatching() => Volatile.Write(ref _matching, 0);

    public void Dispose()
    {
        lock (_lock)
        {
            _watching?.Dispose();
            _watching = null;
        }

        Turn.Dispose();
    }
}
