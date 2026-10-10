using MMDb.Domain.Movies;

namespace MMDb.Application.Vault;

/// <summary>
/// Port: writes the collection into an Obsidian vault as markdown (one note per film, an index, a guide for
/// Claude and Bases views). It only ever changes or removes files it wrote itself.
/// </summary>
public interface IVaultWriter
{
    /// <summary>
    /// Brings the whole vault in line with the collection: rewrites what is out of date and removes the notes
    /// of films no longer in it.
    /// </summary>
    /// <param name="movies">Every film in the collection.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task SyncAsync(string folder, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default);

    /// <summary>Writes one film's note and cover (moving the note if the title changed), then the index.</summary>
    /// <param name="movies">Every film in the collection, this one included.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task WriteMovieAsync(string folder, Movie movie, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default);

    /// <summary>Removes a film's note and cover, then rewrites the index.</summary>
    /// <param name="movies">Every film still in the collection.</param>
    /// <exception cref="VaultUnavailableException">The folder is missing or can't be written.</exception>
    Task RemoveMovieAsync(string folder, Guid movieId, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default);

    /// <returns>The film's note relative to the vault (<c>Movies/Heat (1995).md</c>), if one has been written.</returns>
    string? NoteOf(string folder, Guid movieId);
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
