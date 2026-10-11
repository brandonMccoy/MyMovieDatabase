using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Application.Vault;

public enum VaultState
{
    /// <summary>The user doesn't keep a vault (or has turned it off): nothing is written.</summary>
    Off,

    /// <summary>The vault is on, but no folder has been chosen for it yet.</summary>
    NotSetUp,
    Syncing,
    Synced,

    /// <summary>The last write failed; the next change or a rebuild writes everything again.</summary>
    Failed,
}

/// <summary>Which vault is in use and how the last write to it went.</summary>
public sealed record VaultStatus
{
    public required VaultState State { get; init; }

    public string? Folder { get; init; }

    /// <summary>When the vault was last brought up to date.</summary>
    public DateTimeOffset? LastSyncAt { get; init; }

    /// <summary>Notes Shelf keeps in the vault (one per film and per comic).</summary>
    public int NoteCount { get; init; }

    public VaultProblem? Problem { get; init; }

    /// <summary>What went wrong, for the Settings screen.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Keeps the Obsidian vault in step with the collection. One per app: writes are taken one at a time, and
/// each one lists the collection inside that turn so a slower write can never overwrite a newer one.
/// A failed write never fails the save that caused it; it shows in <see cref="Status"/> instead.
/// </summary>
public sealed class VaultMirror(IVaultWriter writer, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private volatile VaultStatus _status = new() { State = VaultState.Off };
    private int _held;
    private bool _missed;

    public event Action? Changed;

    public VaultStatus Status => _status;

    /// <returns>The film's or comic's note relative to the vault, if there is a vault and the note has been written.</returns>
    public string? NoteOf(Guid id) => Status.Folder is { } folder ? writer.NoteOf(folder, id) : null;

    /// <summary>Stops writing, once a write under way has finished. The folder and what is in it are left as they are.</summary>
    internal async Task TurnOffAsync(CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            Publish(new() { State = VaultState.Off });
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Turns the vault on at <paramref name="folder"/> (<see langword="null"/>: none chosen yet) and writes it all.</summary>
    internal async Task SyncAsync(string? folder, VaultContentSource source, CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            if (folder is null)
            {
                Publish(new() { State = VaultState.NotSetUp });
                return;
            }

            var before = Status;
            Publish(new() { State = VaultState.Syncing, Folder = folder });
            try
            {
                await WriteAsync(folder, async all => await writer.SyncAsync(folder, all, await source.LoadFilmsAsync(cancellationToken), cancellationToken), source, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Publish(before);
                throw;
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Many saves are coming (a folder of films being imported): until <see cref="ReleaseAsync"/>, changes aren't
    /// written one by one, each with the index and the other shared files, but all at once at the end.
    /// </summary>
    internal void Hold() => Interlocked.Increment(ref _held);

    /// <summary>Ends a <see cref="Hold"/> and writes what changed meanwhile.</summary>
    internal async Task ReleaseAsync(VaultContentSource source)
    {
        if (Interlocked.Decrement(ref _held) == 0 && Interlocked.Exchange(ref _missed, false) && Status.Folder is { } folder)
        {
            await SyncAsync(folder, source, CancellationToken.None);
        }
    }

    internal Task MovieSavedAsync(Movie movie, VaultContentSource source) =>
        ChangeAsync((folder, all) => writer.WriteMovieAsync(folder, movie, all), source);

    internal Task ComicSavedAsync(Comic comic, VaultContentSource source) =>
        ChangeAsync((folder, all) => writer.WriteComicAsync(folder, comic, all), source);

    /// <summary>Something was put on the wishlist, taken off it, or changed on it.</summary>
    internal Task WishlistChangedAsync(VaultContentSource source) =>
        ChangeAsync((folder, all) => writer.WriteWishlistAsync(folder, all), source);

    /// <summary>A film or comic was deleted.</summary>
    internal Task RemovedAsync(Guid id, VaultContentSource source) =>
        ChangeAsync((folder, all) => writer.RemoveAsync(folder, id, all), source);

    // Not cancellable: the change is already in the database, so the vault should follow it.
    private async Task ChangeAsync(Func<string, VaultContent, Task> change, VaultContentSource source)
    {
        if (Status.Folder is null)
        {
            return;
        }

        if (Volatile.Read(ref _held) > 0)
        {
            Volatile.Write(ref _missed, true);
            return;
        }

        await _turn.WaitAsync();
        try
        {
            if (Status.Folder is not { } folder)
            {
                return;
            }

            // After a failure other notes may be stale too, so catch up on everything.
            Func<VaultContent, Task> write = Status.State == VaultState.Failed
                ? async all => await writer.SyncAsync(folder, all, await source.LoadFilmsAsync())
                : all => change(folder, all);
            await WriteAsync(folder, write, source, CancellationToken.None);
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task WriteAsync(
        string folder,
        Func<VaultContent, Task> write,
        VaultContentSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var all = await source.LoadAsync(cancellationToken);
            await write(all);
            Publish(new() { State = VaultState.Synced, Folder = folder, LastSyncAt = clock.GetUtcNow(), NoteCount = all.NoteCount });
        }
        catch (VaultUnavailableException e)
        {
            Fail(folder, e.Problem, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(folder, VaultProblem.CannotWrite, e.Message);
        }
    }

    private void Fail(string folder, VaultProblem problem, string detail) =>
        Publish(Status with { State = VaultState.Failed, Folder = folder, Problem = problem, Detail = detail });

    public void Dispose() => _turn.Dispose();

    private void Publish(VaultStatus status)
    {
        _status = status;
        Changed?.Invoke();
    }
}
