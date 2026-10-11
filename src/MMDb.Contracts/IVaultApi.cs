using MMDb.Contracts.Vault;

namespace MMDb.Contracts;

/// <summary>The Obsidian vault Shelf mirrors the collection into. PC only: the vault is a folder on the PC.</summary>
public interface IVaultApi
{
    event Action? StatusChanged;

    VaultStatusDto Status { get; }

    /// <summary>If the vault is on, picks up the folder chosen last time and catches the vault up. Called once at startup.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the vault on or off. On: writes the whole collection into the folder chosen before, if there is one.
    /// Off: nothing more is written, and what is in the folder stays.
    /// </summary>
    Task SetUseAsync(bool use, CancellationToken cancellationToken = default);

    /// <summary>Remembers the folder, turns the vault on and writes the whole collection into it.</summary>
    /// <returns><see langword="false"/> if there is no such folder.</returns>
    Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Rewrites everything Shelf keeps in the vault.</summary>
    Task RebuildAsync(CancellationToken cancellationToken = default);

    /// <returns>The film's note, if there is a vault and the note has been written.</returns>
    VaultNoteDto? NoteFor(Guid movieId);
}
