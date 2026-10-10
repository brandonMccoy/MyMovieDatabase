using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Wishlist;

namespace MMDb.Contracts;

/// <summary>
/// What the UI can ask of the collection. On the PC this calls the application directly;
/// on the phone it goes over the LAN to the PC.
/// </summary>
public interface ICollectionApi
{
    /// <summary>
    /// Something was added, changed or removed. On the PC this also tells of what a phone saved, so open screens
    /// can show it; it may be raised on any thread.
    /// </summary>
    event Action? Changed;

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

    Task<ComicListResponse> ListComicsAsync(ComicListRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> if there is no such comic.</returns>
    Task<ComicDto?> GetComicAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SaveComicResponse> AddComicAsync(SaveComicRequest request, CancellationToken cancellationToken = default);

    /// <param name="version">The <see cref="ComicDto.Version"/> that was edited.</param>
    Task<SaveComicResponse> UpdateComicAsync(Guid id, int version, SaveComicRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> if there was no such comic.</returns>
    Task<bool> DeleteComicAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks a comic up online (Metron for issues, Open Library for an ISBN) without saving anything.</summary>
    /// <param name="query">Series words and an issue number ("saga 7"), a cover barcode, a UPC and issue number, or an ISBN.</param>
    Task<ComicLookupResponse> LookupComicAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Looks a scanned barcode up as a film or a comic, whichever the code is, without saving anything.</summary>
    Task<ScanResponse> LookupScanAsync(ScanRequest request, CancellationToken cancellationToken = default);

    /// <param name="search">Words to look for in titles, directors and years; <see langword="null"/> lists everything.</param>
    Task<WishlistResponse> ListWishlistAsync(string? search = null, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    Task<WishlistMovieDto?> GetWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default);

    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    Task<WishlistComicDto?> GetWishlistComicAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Wants a film already on the wishlist on another format.</summary>
    Task<WantMovieResponse> ChangeWishlistFormatAsync(Guid id, MovieFormatKind format, CancellationToken cancellationToken = default);

    /// <summary>Puts a film on the wishlist. A film already on it stays one item, now wanted on this format.</summary>
    Task<WantMovieResponse> WantMovieAsync(WantMovieRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> if it wasn't on the wishlist.</returns>
    Task<bool> RemoveWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks a film on the wishlist up online to add it to the collection ("Got it"), without saving anything.</summary>
    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    Task<MovieLookupResponse?> LookupWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Puts an issue or a collected edition on the wishlist. Something already wanted stays as it is.</summary>
    Task<WantComicResponse> WantComicAsync(WantComicRequest request, CancellationToken cancellationToken = default);

    /// <summary>Puts every issue of a run missing from a series on the wishlist, each as an item of its own.</summary>
    Task<WantComicResponse> WantMissingIssuesAsync(WantMissingRequest request, CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> if it wasn't on the wishlist.</returns>
    Task<bool> RemoveWishlistComicAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks a comic on the wishlist up online to add it to the collection ("Got it"), without saving anything.</summary>
    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    Task<ComicLookupResponse?> LookupWishlistComicAsync(Guid id, CancellationToken cancellationToken = default);
}
