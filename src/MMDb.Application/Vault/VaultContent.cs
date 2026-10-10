using MMDb.Application.Comics;
using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Vault;

/// <summary>
/// Everything the vault mirrors: every film (as its index lists them), every comic, what Metron says about
/// their series, and the wishlist.
/// </summary>
public sealed record VaultContent(IReadOnlyList<MovieListing> Movies, IReadOnlyList<Comic> Comics, IReadOnlyList<ComicSeries> Series)
{
    public static readonly VaultContent Empty = new([], [], []);

    public IReadOnlyList<WishlistMovie> WantedMovies { get; init; } = [];

    public IReadOnlyList<WishlistComic> WantedComics { get; init; } = [];

    /// <summary>Notes Shelf keeps in the vault: one per film, per comic and per wishlist item.</summary>
    public int NoteCount => Movies.Count + Comics.Count + WantedMovies.Count + WantedComics.Count;
}

/// <summary>Reads the <see cref="VaultContent"/> from this unit of work's repositories.</summary>
public sealed class VaultContentSource(IMovieRepository movies, IComicRepository comics, IWishlistRepository wishlist)
{
    public async Task<VaultContent> LoadAsync(CancellationToken cancellationToken = default) =>
        new(await movies.ListAsync(cancellationToken), await comics.ListAsync(cancellationToken), await comics.ListSeriesAsync(cancellationToken))
        {
            WantedMovies = await wishlist.ListMoviesAsync(cancellationToken),
            WantedComics = await wishlist.ListComicsAsync(cancellationToken),
        };

    /// <summary>Every film in full, credits and all: what it takes to write every film's note again.</summary>
    public Task<IReadOnlyList<Movie>> LoadFilmsAsync(CancellationToken cancellationToken = default) => movies.ListInFullAsync(cancellationToken);
}
