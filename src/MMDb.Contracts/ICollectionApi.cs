using MMDb.Contracts.Movies;

namespace MMDb.Contracts;

/// <summary>
/// What the UI can ask of the collection. On the PC this calls the application directly;
/// on the phone it goes over the LAN to the PC.
/// </summary>
public interface ICollectionApi
{
    Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> if there is no such film.</returns>
    Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default);

    /// <param name="version">The <see cref="MovieDto.Version"/> that was edited.</param>
    Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> if there was no such film.</returns>
    Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks a film up online (TMDB, and UPCitemdb for a barcode) without saving anything.</summary>
    /// <param name="query">A title, optionally followed by the year ("heat 1995"), or a UPC/EAN.</param>
    Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default);
}
