using MMDb.Application.Movies;
using MMDb.Contracts.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Server.Movies;

/// <summary>Translates between the wire contracts and the domain.</summary>
internal static class MovieMapping
{
    public static MovieFormatKind ToContract(this MediaFormat format) => format switch
    {
        MediaFormat.Uhd4K => MovieFormatKind.Uhd4K,
        MediaFormat.BluRay => MovieFormatKind.BluRay,
        MediaFormat.Dvd => MovieFormatKind.Dvd,
        MediaFormat.Digital => MovieFormatKind.Digital,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    // Unknown values pass through as undefined formats, which validation reports.
    public static MediaFormat ToDomain(this MovieFormatKind format) => format switch
    {
        MovieFormatKind.Uhd4K => MediaFormat.Uhd4K,
        MovieFormatKind.BluRay => MediaFormat.BluRay,
        MovieFormatKind.Dvd => MediaFormat.Dvd,
        MovieFormatKind.Digital => MediaFormat.Digital,
        _ => (MediaFormat)(-1),
    };

    public static MovieQuery ToDomain(this MovieListRequest request) => new()
    {
        Search = request.Search,
        Format = request.Format?.ToDomain(),
        Genre = string.IsNullOrWhiteSpace(request.Genre) ? null : request.Genre,
        Decade = request.Decade,
        Sort = request.Sort switch
        {
            MovieSortOrder.Title => MovieSort.Title,
            MovieSortOrder.Year => MovieSort.Year,
            _ => MovieSort.RecentlyAdded,
        },
    };

    public static MovieDetails ToDetails(this SaveMovieRequest request) => new()
    {
        Title = request.Title ?? "",
        Year = request.Year,
        Directors = request.Directors ?? [],
        Cast = [.. (request.Cast ?? []).Select(c => new CastMember(c.Name, c.Character))],
        Genres = request.Genres ?? [],
        RuntimeMinutes = request.RuntimeMinutes,
        Rating = request.Rating,
        Overview = request.Overview,
        TmdbId = request.TmdbId,
        TrailerYouTubeKey = request.TrailerYouTubeKey,
    };

    public static FormatDetails[] ToFormats(this SaveMovieRequest request) =>
        [.. (request.Formats ?? []).Select(f => new FormatDetails(f.Format.ToDomain(), f.Upc, f.FilePath))];

    public static MovieDto ToDto(this Movie movie) => new()
    {
        Id = movie.Id,
        Version = movie.Version,
        Title = movie.Title,
        Year = movie.Year,
        Directors = movie.Directors,
        Cast = [.. movie.Cast.Select(c => new CastMemberDto(c.Name, c.Character))],
        Genres = movie.Genres,
        RuntimeMinutes = movie.RuntimeMinutes,
        Rating = movie.Rating,
        Overview = movie.Overview,
        TmdbId = movie.TmdbId,
        TrailerYouTubeKey = movie.TrailerYouTubeKey,
        TrailerUrl = Trailer.UrlFor(movie.Title, movie.Year, movie.TrailerYouTubeKey),
        Formats = [.. movie.Formats.Select(f => new MovieFormatDto(f.Format.ToContract(), f.Upc?.Value, f.FilePath, f.AddedAt))],
        CreatedAt = movie.CreatedAt,
        UpdatedAt = movie.UpdatedAt,
    };

    public static MovieSummaryDto ToSummary(this Movie movie) => new(
        movie.Id,
        movie.Title,
        movie.Year,
        movie.Directors,
        [.. movie.Formats.Select(f => f.Format.ToContract())],
        Trailer.UrlFor(movie.Title, movie.Year, movie.TrailerYouTubeKey));

    public static MovieListResponse ToContract(this MovieList list) => new(
        [.. list.Movies.Select(ToSummary)],
        new MovieFacetsDto(
            list.Facets.FilmCount,
            list.Facets.DiscCount,
            list.Facets.MatchingCount,
            list.Facets.FormatCounts.ToDictionary(p => p.Key.ToContract(), p => p.Value),
            list.Facets.Genres,
            list.Facets.Decades));

    public static SaveMovieResponse ToContract(this SaveMovieResult result) => result switch
    {
        SaveMovieResult.Saved saved => new(SaveStatus.Saved, saved.Movie.ToDto()),
        SaveMovieResult.Invalid invalid => new(SaveStatus.Invalid, Errors: [.. invalid.Errors.Select(e => new FieldError(e.Field, e.Message))]),
        SaveMovieResult.NotFound => new(SaveStatus.NotFound),
        SaveMovieResult.Conflict => new(SaveStatus.Conflict),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
