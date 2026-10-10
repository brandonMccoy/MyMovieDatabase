using MMDb.Contracts.Digital;

namespace MMDb.Contracts;

/// <summary>
/// Digital films: the movie folder on the PC, the video files in it waiting to be added, and playing a film's
/// file. PC only: the files are on the PC.
/// </summary>
public interface IDigitalApi
{
    /// <summary><see cref="Folder"/> changed; may be raised on any thread.</summary>
    event Action? Changed;

    MovieFolderDto Folder { get; }

    /// <summary>Picks up the folder chosen last time, scans it and starts watching it. Called once at startup.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Remembers the movie folder and scans it.</summary>
    /// <returns><see langword="false"/> if there is no such folder.</returns>
    Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default);

    Task SetWatchAsync(bool watch, CancellationToken cancellationToken = default);

    /// <summary>Reads the folder again. Nothing is asked of TMDB.</summary>
    Task RescanAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up on TMDB the waiting files that haven't been yet; <see cref="Folder"/> shows how far it is.</summary>
    Task MatchAsync(CancellationToken cancellationToken = default);

    /// <summary>Searches TMDB for the film a file is.</summary>
    /// <param name="query">A title, optionally followed by the year.</param>
    Task<FilmSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Says which film a waiting file is.</summary>
    Task ChooseAsync(string path, FilmHitDto film, CancellationToken cancellationToken = default);

    void Tick(string path, bool ticked);

    /// <summary>Takes a waiting file off the list for good.</summary>
    Task SkipAsync(string path, CancellationToken cancellationToken = default);

    Task BringBackAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Adds every ticked file to the collection.</summary>
    /// <returns>How many were added.</returns>
    Task<int> AddTickedAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes a waiting file the file of its film, which already has another; that one is skipped from then on.</summary>
    Task<bool> UseInsteadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Says where a film's missing file is now.</summary>
    /// <returns><see langword="false"/> if it isn't a video file that exists.</returns>
    Task<bool> LocateAsync(Guid movieId, string path, CancellationToken cancellationToken = default);

    /// <summary>Removes a film's digital copy; a film owned on nothing else leaves the collection.</summary>
    Task<bool> RemoveCopyAsync(Guid movieId, CancellationToken cancellationToken = default);

    /// <summary>Opens a film's video file in the PC's player.</summary>
    /// <returns><see langword="false"/> if there is no file to play or the PC could not open it.</returns>
    Task<bool> PlayAsync(Guid movieId, CancellationToken cancellationToken = default);

    bool FileExists(string path);
}
