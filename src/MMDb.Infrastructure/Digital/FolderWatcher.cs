using MMDb.Application.Digital;

namespace MMDb.Infrastructure.Digital;

/// <summary><see cref="IFolderWatcher"/> over the file system's own change notices.</summary>
internal sealed class FolderWatcher : IFolderWatcher
{
    /// <summary>
    /// How long things must stay quiet before the listener is told: copying a film raises a burst of notices,
    /// and moving a folder of them raises many.
    /// </summary>
    public TimeSpan Quiet { get; init; } = TimeSpan.FromSeconds(2);

    public IDisposable Watch(string folder, Action changed)
    {
        try
        {
            return new Watching(folder, changed, Quiet);
        }
        catch (ArgumentException e)
        {
            // FileSystemWatcher's way of saying the folder isn't there.
            throw new DirectoryNotFoundException(e.Message, e);
        }
    }

    private sealed class Watching : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _quiet;
        private readonly TimeSpan _wait;

        public Watching(string folder, Action changed, TimeSpan wait)
        {
            _wait = wait;
            _quiet = new Timer(_ => changed());
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };

            // Which file it was doesn't matter: the listener scans the folder again. An error (too many
            // changes at once to keep up with) means some were missed, which a scan covers too.
            _watcher.Created += (_, _) => Stir();
            _watcher.Deleted += (_, _) => Stir();
            _watcher.Renamed += (_, _) => Stir();
            _watcher.Error += (_, _) => Stir();
            _watcher.EnableRaisingEvents = true;
        }

        public void Dispose()
        {
            _watcher.Dispose();
            _quiet.Dispose();
        }

        private void Stir()
        {
            try
            {
                _quiet.Change(_wait, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // A last notice after watching stopped.
            }
        }
    }
}
