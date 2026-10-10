using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Application.Vault;

/// <summary>
/// Port: writes the collection into an Obsidian vault as markdown (one note per film and per comic, a note per
/// comic series, an index, a guide for Claude and Bases views). It only ever changes or removes files it wrote itself.
/// </summary>
public interface IVaultWriter
{
    /// <summary>
    /// Brings the whole vault in line with the collection: rewrites what is out of date and removes the notes
    /// of films and comics no longer in it.
    /// </summary>
    /// <param name="films">Every film in <paramref name="content"/> in full, to write their notes from.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task SyncAsync(string folder, VaultContent content, IReadOnlyList<Movie> films, CancellationToken cancellationToken = default);

    /// <summary>Writes one film's note and cover (moving the note if the title changed), then the shared files.</summary>
    /// <param name="content">The whole collection, this film included.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task WriteMovieAsync(string folder, Movie movie, VaultContent content, CancellationToken cancellationToken = default);

    /// <summary>Writes one comic's note and cover (moving the note if its series or number changed), then the series notes and shared files.</summary>
    /// <param name="content">The whole collection, this comic included.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task WriteComicAsync(string folder, Comic comic, VaultContent content, CancellationToken cancellationToken = default);

    /// <summary>Removes a film's or comic's note and cover, then rewrites the series notes and shared files.</summary>
    /// <param name="content">The whole collection still owned.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task RemoveAsync(string folder, Guid id, VaultContent content, CancellationToken cancellationToken = default);

    /// <returns>The note relative to the vault (<c>Movies/Heat (1995).md</c>), if one has been written.</returns>
    string? NoteOf(string folder, Guid id);
}

/// <summary>Port: small values the PC app keeps between runs (the vault folder).</summary>
public interface ISettingStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <param name="value"><see langword="null"/> removes the setting.</param>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

public enum VaultProblem
{
    /// <summary>The vault folder isn't there (renamed, or on a drive that isn't connected).</summary>
    FolderMissing,

    /// <summary>A file could not be written or removed (no permission, in use, disk full).</summary>
    CannotWrite,
}

/// <summary>The vault could not be written.</summary>
public sealed class VaultUnavailableException(VaultProblem problem, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public VaultProblem Problem { get; } = problem;
}
