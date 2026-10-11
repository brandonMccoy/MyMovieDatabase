namespace MMDb.Contracts.Vault;

public enum VaultSyncState
{
    /// <summary>The vault isn't in use: nothing is written.</summary>
    Off,

    /// <summary>On, but no folder has been chosen yet.</summary>
    NotSetUp,
    Syncing,
    Synced,
    Failed,
}

public enum VaultIssue
{
    /// <summary>The folder isn't there (renamed, or on a drive that isn't connected).</summary>
    FolderMissing,

    /// <summary>A file could not be written (no permission, in use, disk full).</summary>
    CannotWrite,
}

/// <param name="NoteCount">Notes Shelf keeps in the vault.</param>
/// <param name="Detail">What went wrong, when <paramref name="State"/> is <see cref="VaultSyncState.Failed"/>.</param>
public sealed record VaultStatusDto(
    VaultSyncState State,
    string? Folder = null,
    DateTimeOffset? LastSyncAt = null,
    int NoteCount = 0,
    VaultIssue? Issue = null,
    string? Detail = null);

/// <param name="Path">Relative to the vault: <c>Movies/Heat (1995).md</c>.</param>
/// <param name="OpenUri">Opens the note in Obsidian.</param>
public sealed record VaultNoteDto(string Path, Uri OpenUri);
