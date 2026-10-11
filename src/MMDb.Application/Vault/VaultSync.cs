namespace MMDb.Application.Vault;

/// <summary>Use cases for the vault: pick up the saved folder at startup, turn it on or off, choose a folder, rebuild.</summary>
public sealed class VaultSync(VaultContentSource content, ISettingStore settings, VaultMirror mirror)
{
    internal const string FolderSetting = "vault.folder";
    internal const string UseSetting = "vault.use";

    /// <summary>If the vault is on, uses the folder chosen last time, if any, and catches up on anything a failed write missed.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var folder = await settings.GetAsync(FolderSetting, cancellationToken);

        // Off unless turned on. A folder chosen before there was a switch stays in use.
        var on = await settings.GetAsync(UseSetting, cancellationToken) is { } use ? use == "on" : folder is not null;
        if (on)
        {
            await mirror.SyncAsync(folder, content, cancellationToken);
        }
    }

    /// <summary>
    /// Turns the vault on or off and remembers it. On: writes the whole collection into the folder chosen before,
    /// if there is one. Off: nothing more is written; the folder stays chosen and everything in it stays as it is.
    /// </summary>
    public async Task SetUseAsync(bool use, CancellationToken cancellationToken = default)
    {
        await settings.SetAsync(UseSetting, use ? "on" : "off", cancellationToken);
        if (use)
        {
            await mirror.SyncAsync(await settings.GetAsync(FolderSetting, cancellationToken), content, cancellationToken);
        }
        else
        {
            await mirror.TurnOffAsync(cancellationToken);
        }
    }

    /// <summary>Remembers the folder, turns the vault on and writes the whole collection into it.</summary>
    /// <returns><see langword="false"/> (and nothing changed) if there is no such folder.</returns>
    public async Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        await settings.SetAsync(FolderSetting, folder, cancellationToken);
        await settings.SetAsync(UseSetting, "on", cancellationToken);
        await mirror.SyncAsync(folder, content, cancellationToken);
        return true;
    }

    /// <summary>Rewrites everything Shelf keeps in the vault; does nothing if the vault is off or no folder is set.</summary>
    public Task RebuildAsync(CancellationToken cancellationToken = default) =>
        mirror.Status.Folder is { } folder ? mirror.SyncAsync(folder, content, cancellationToken) : Task.CompletedTask;
}
