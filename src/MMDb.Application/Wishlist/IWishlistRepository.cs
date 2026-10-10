using MMDb.Application.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Wishlist;

/// <summary>
/// Port: where the wishlist is stored. It shares its unit of work with the collection's repositories, so a film
/// saved and the wish it grants are written together.
/// </summary>
public interface IWishlistRepository
{
    /// <summary>Read-only: every film on the wishlist.</summary>
    Task<IReadOnlyList<WishlistMovie>> ListMoviesAsync(CancellationToken cancellationToken = default);

    Task<WishlistMovie?> FindMovieAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The wishes that may be for films with these TMDB ids: the ones linked to one of them, and the ones entered
    /// without a TMDB id (told apart by <see cref="WishlistMovie.IsFor(int?, string, int?)"/>).
    /// </summary>
    Task<IReadOnlyList<WishlistMovie>> FindMoviesForAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken = default);

    /// <summary>The films wanted that have a poster on TMDB but none kept here yet, to try for it again.</summary>
    Task<IReadOnlyList<WishlistMovie>> FindMoviesMissingCoverAsync(CancellationToken cancellationToken = default);

    void Add(WishlistMovie movie);

    void Remove(WishlistMovie movie);

    /// <summary>Read-only: every comic on the wishlist.</summary>
    Task<IReadOnlyList<WishlistComic>> ListComicsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every comic on the wishlist, to change or remove: which of them a comic grants is told in memory
    /// (see <see cref="WishlistComic.IsFor"/>), and a wishlist is short.
    /// </summary>
    Task<IReadOnlyList<WishlistComic>> LoadComicsAsync(CancellationToken cancellationToken = default);

    Task<WishlistComic?> FindComicAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(WishlistComic comic);

    void Remove(WishlistComic comic);

    /// <exception cref="ConcurrencyConflictException">The item was changed or removed by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
