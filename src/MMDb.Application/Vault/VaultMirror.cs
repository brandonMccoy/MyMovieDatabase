using MMDb.Domain.Movies;

namespace MMDb.Application.Vault;

public enum VaultState
{
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

    /// <summary>Notes Shelf keeps in the vault (one per film).</summary>
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
    private volatile VaultStatus _status = new() { State = VaultState.NotSetUp };

    public event Action? Changed;

    public VaultStatus Status => _status;

    /// <returns>The film's note relative to the vault, if there is a vault and the note has been written.</returns>
    public string? NoteOf(Guid movieId) => Status.Folder is { } folder ? writer.NoteOf(folder, movieId) : null;

    /// <summary>Switches to <paramref name="folder"/> (<see langword="null"/>: no vault) and writes it all.</summary>
    internal async Task SyncAsync(string? folder, Func<CancellationToken, Task<IReadOnlyList<Movie>>> movies, CancellationToken cancellationToken)
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
                await WriteAsync(folder, all => writer.SyncAsync(folder, all, cancellationToken), movies, cancellationToken);
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

    internal Task MovieSavedAsync(Movie movie, Func<CancellationToken, Task<IReadOnlyList<Movie>>> movies) =>
        ChangeAsync((folder, all) => writer.WriteMovieAsync(folder, movie, all), movies);

    internal Task MovieRemovedAsync(Guid movieId, Func<CancellationToken, Task<IReadOnlyList<Movie>>> movies) =>
        ChangeAsync((folder, all) => writer.RemoveMovieAsync(folder, movieId, all), movies);

    // Not cancellable: the change is already in the database, so the vault should follow it.
    private async Task ChangeAsync(Func<string, IReadOnlyList<Movie>, Task> change, Func<CancellationToken, Task<IReadOnlyList<Movie>>> movies)
    {
        if (Status.Folder is null)
        {
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
            Func<IReadOnlyList<Movie>, Task> write = Status.State == VaultState.Failed
                ? all => writer.SyncAsync(folder, all)
                : all => change(folder, all);
            await WriteAsync(folder, write, movies, CancellationToken.None);
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task WriteAsync(
        string folder,
        Func<IReadOnlyList<Movie>, Task> write,
        Func<CancellationToken, Task<IReadOnlyList<Movie>>> movies,
        CancellationToken cancellationToken)
    {
        try
        {
            var all = await movies(cancellationToken);
            await write(all);
            Publish(new() { State = VaultState.Synced, Folder = folder, LastSyncAt = clock.GetUtcNow(), NoteCount = all.Count });
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
