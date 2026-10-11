using MMDb.Application.Wishlist;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;
using MMDb.Local.Comics;
using MMDb.Local.Movies;

namespace MMDb.Local.Wishlist;

/// <summary>Translates between the wire contracts and the domain.</summary>
internal static class WishlistMapping
{
    public static WishlistResponse ToContract(this WishlistContents wishlist, CoverLinks covers) =>
        new([.. wishlist.Movies.Select(m => m.ToDto(covers))], wishlist.MovieCount, [.. wishlist.Comics.Select(c => c.ToDto(covers))], wishlist.ComicCount);

    public static WishlistComicDto ToDto(this WishlistComic comic, CoverLinks covers) => new()
    {
        Id = comic.Id,
        DisplayName = comic.DisplayName,
        Format = comic.Format.ToContract(),
        Series = comic.Series,
        SeriesYear = comic.SeriesYear,
        Number = comic.Number,
        Publisher = comic.Publisher,
        FromMissing = comic.FromMissing,
        CoverUrl = covers.For(comic.CoverImage),
        AddedAt = comic.CreatedAt,
    };

    public static ComicWishDto ToWish(this WishlistComic comic) => new(comic.Id, comic.DisplayName, comic.Series, comic.SeriesYear, comic.Number);

    public static WantedComic ToDomain(this WantComicRequest request) => new()
    {
        Format = request.Format.ToDomain(),
        Series = request.Series,
        SeriesYear = request.SeriesYear,
        Number = request.Number,
        Title = request.Title,
        Publisher = request.Publisher,
        Isbn = request.Isbn,
        MetronId = request.MetronId,
        MetronSeriesId = request.MetronSeriesId,
        CoverUrl = request.CoverSource,
    };

    public static WantComicResponse ToContract(this WantComicResult result, CoverLinks covers) => result switch
    {
        WantComicResult.Saved saved => new(SaveStatus.Saved, saved.Comic.ToDto(covers)),
        WantComicResult.AlreadyOwned owned => new(SaveStatus.AlreadyOwned, Owned: owned.Comic.ToSummary(covers)),
        WantComicResult.Invalid invalid => new(SaveStatus.Invalid, Errors: [.. invalid.Errors.Select(e => new FieldError(e.Field, e.Message))]),
        WantComicResult.NotFound => new(SaveStatus.NotFound),
        WantComicResult.Conflict => new(SaveStatus.Conflict),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    public static WishlistMovieDto ToDto(this WantedFilm film, CoverLinks covers) => new()
    {
        Id = film.Item.Id,
        Title = film.Item.Title,
        Year = film.Item.Year,
        Directors = film.Item.Directors,
        Format = film.Item.Format.ToContract(),
        OwnedFormats = [.. film.Owned.Select(f => f.ToContract())],
        CoverUrl = covers.For(film.Item.CoverImage),
        TrailerUrl = Trailer.UrlFor(film.Item.Title, film.Item.Year, film.Item.TrailerYouTubeKey),
        AddedAt = film.Item.CreatedAt,
    };

    public static MovieWishDto ToWish(this WishlistMovie item) => new(item.Id, item.Format.ToContract(), item.Title, item.Year);

    public static WantedMovie ToDomain(this WantMovieRequest request) => new()
    {
        Title = request.Title ?? "",
        Year = request.Year,
        Directors = request.Directors ?? [],
        TmdbId = request.TmdbId,
        TrailerYouTubeKey = request.TrailerYouTubeKey,
        PosterPath = request.PosterPath,
        Format = request.Format.ToDomain(),
    };

    public static WantMovieResponse ToContract(this WantMovieResult result, CoverLinks covers) => result switch
    {
        WantMovieResult.Saved saved => new(SaveStatus.Saved, saved.Film.ToDto(covers)),
        WantMovieResult.AlreadyOwned owned => new(SaveStatus.AlreadyOwned, Owned: owned.Movie.ToSummary(covers)),
        WantMovieResult.Invalid invalid => new(SaveStatus.Invalid, Errors: [.. invalid.Errors.Select(e => new FieldError(e.Field, e.Message))]),
        WantMovieResult.NotFound => new(SaveStatus.NotFound),
        WantMovieResult.Conflict => new(SaveStatus.Conflict),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
