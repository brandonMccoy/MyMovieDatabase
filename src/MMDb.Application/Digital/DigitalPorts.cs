namespace MMDb.Application.Digital;

/// <summary>Port: the video files the user said never to offer for import again (extras, samples, home videos).</summary>
public interface ISkippedFileStore
{
    /// <summary>Full paths, in no particular order.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Does nothing if the file is skipped already.</summary>
    Task AddAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Does nothing if the file isn't skipped.</summary>
    Task RemoveAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Port: notices files arriving in, leaving or moving inside a folder and everything under it.</summary>
public interface IFolderWatcher
{
    /// <param name="changed">
    /// Called, on any thread, once things have been quiet for a moment after one or more changes; what changed
    /// is for the listener to find out.
    /// </param>
    /// <returns>Dispose to stop watching.</returns>
    /// <exception cref="IOException">The folder can't be watched (it is gone, or on a drive that doesn't tell).</exception>
    IDisposable Watch(string folder, Action changed);
}

/// <summary>Port: opens a file in whatever program the PC uses for it (a video in the user's player).</summary>
public interface IFileOpener
{
    /// <returns><see langword="false"/> if the PC could not open it.</returns>
    bool Open(string path);
}
