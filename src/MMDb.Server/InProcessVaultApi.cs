using MMDb.Application.Vault;
using MMDb.Contracts;
using MMDb.Contracts.Vault;

namespace MMDb.Server;

/// <summary><see cref="IVaultApi"/> for the PC app: calls the vault use cases directly, one DI scope per call.</summary>
public sealed class InProcessVaultApi(IServiceScopeFactory scopes, VaultMirror mirror) : IVaultApi
{
    public event Action? StatusChanged
    {
        add => mirror.Changed += value;
        remove => mirror.Changed -= value;
    }

    public VaultStatusDto Status => ToDto(mirror.Status);

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        With(sync => sync.StartAsync(cancellationToken));

    public Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<VaultSync>().SetFolderAsync(folder, cancellationToken);
            },
            cancellationToken);

    public Task RebuildAsync(CancellationToken cancellationToken = default) =>
        With(sync => sync.RebuildAsync(cancellationToken));

    public VaultNoteDto? NoteFor(Guid movieId)
    {
        if (mirror.Status.Folder is not { } folder || mirror.NoteOf(movieId) is not { } note)
        {
            return null;
        }

        // Obsidian finds the vault from the note's full path.
        var path = Path.GetFullPath(Path.Combine(folder, note));
        return new(note, new Uri("obsidian://open?path=" + Uri.EscapeDataString(path)));
    }

    private static VaultStatusDto ToDto(VaultStatus status) => new(
        status.State switch
        {
            VaultState.NotSetUp => VaultSyncState.NotSetUp,
            VaultState.Syncing => VaultSyncState.Syncing,
            VaultState.Synced => VaultSyncState.Synced,
            _ => VaultSyncState.Failed,
        },
        status.Folder,
        status.LastSyncAt,
        status.NoteCount,
        status.Problem switch
        {
            null => null,
            VaultProblem.FolderMissing => VaultIssue.FolderMissing,
            _ => VaultIssue.CannotWrite,
        },
        status.Detail);

    // Off the UI thread: writing a few thousand notes takes a while.
    private Task With(Func<VaultSync, Task> use) =>
        Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            await use(scope.ServiceProvider.GetRequiredService<VaultSync>());
        });
}
