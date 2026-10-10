namespace MMDb.Application.Vault;

/// <summary>Use cases for the vault: pick up the saved folder at startup, choose a folder, rebuild.</summary>
public sealed class VaultSync(VaultContentSource content, ISettingStore settings, VaultMirror mirror)
{
    internal const string FolderSetting = "vault.folder";

    /// <summary>Uses the folder chosen last time, if any, and catches up on anything a failed write missed.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default) =>
        await mirror.SyncAsync(await settings.GetAsync(FolderSetting, cancellationToken), content.LoadAsync, cancellationToken);

    /// <summary>Remembers the folder and writes the whole collection into it.</summary>
    /// <returns><see langword="false"/> (and nothing changed) if there is no such folder.</returns>
    public async Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        await settings.SetAsync(FolderSetting, folder, cancellationToken);
        await mirror.SyncAsync(folder, content.LoadAsync, cancellationToken);
        return true;
    }

    /// <summary>Rewrites everything Shelf keeps in the vault; does nothing if no folder is set.</summary>
    public Task RebuildAsync(CancellationToken cancellationToken = default) =>
        mirror.SyncAsync(mirror.Status.Folder, content.LoadAsync, cancellationToken);
}
