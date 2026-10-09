using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>The movies matching a <see cref="MovieQuery"/>, plus the numbers the library header and filters show.</summary>
public sealed record MovieList(IReadOnlyList<Movie> Movies, MovieFacets Facets);

/// <param name="FilmCount">Films in the whole collection.</param>
/// <param name="DiscCount">Physical copies (every format except Digital) in the whole collection.</param>
/// <param name="MatchingCount">Films matching the search, genre and decade, before the format filter ("All").</param>
/// <param name="FormatCounts">Per format, films matching the search, genre and decade that are owned on it.</param>
/// <param name="Genres">Every genre in the collection, A–Z.</param>
/// <param name="Decades">Every decade in the collection, newest first.</param>
public sealed record MovieFacets(
    int FilmCount,
    int DiscCount,
    int MatchingCount,
    IReadOnlyDictionary<MediaFormat, int> FormatCounts,
    IReadOnlyList<string> Genres,
    IReadOnlyList<int> Decades);
