using System.Globalization;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>
/// Applies a <see cref="MovieQuery"/> in memory. A personal collection is a few thousand films at most,
/// and matching here (rather than in SQL) makes search ignore case and accents: "amelie" finds "Amélie".
/// </summary>
internal static class MovieFilter
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private static readonly string[] LeadingArticles = ["The ", "A ", "An "];

    public static MovieList Apply(IReadOnlyList<Movie> all, MovieQuery query)
    {
        var terms = (query.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matching = all
            .Where(m => terms.All(term => Matches(m, term)))
            .Where(m => query.Genre is null || m.Genres.Contains(query.Genre, StringComparer.OrdinalIgnoreCase))
            .Where(m => query.Decade is null || DecadeOf(m) == query.Decade)
            .ToList();

        var facets = new MovieFacets(
            FilmCount: all.Count,
            DiscCount: all.Sum(m => m.Formats.Count(f => f.Format.IsDisc())),
            MatchingCount: matching.Count,
            FormatCounts: Enum.GetValues<MediaFormat>().ToDictionary(f => f, f => matching.Count(m => m.Owns(f))),
            Genres: [.. GenreOptions(all)],
            Decades: [.. all.Select(DecadeOf).OfType<int>().Distinct().OrderDescending()]);

        var shown = matching.Where(m => query.Format is not { } format || m.Owns(format));
        return new MovieList([.. Sort(shown, query.Sort)], facets);
    }

    private static bool Matches(Movie movie, string term) =>
        Contains(movie.Title, term)
        || movie.Directors.Any(d => Contains(d, term))
        || movie.Cast.Any(c => Contains(c.Name, term))
        || movie.Year?.ToString(CultureInfo.InvariantCulture) == term;

    private static bool Contains(string source, string term) => Compare.IndexOf(source, term, Loose) >= 0;

    // "Crime" and "crime" are one genre; show the most used spelling (ties: the capitalized one).
    private static IEnumerable<string> GenreOptions(IEnumerable<Movie> movies) =>
        movies.SelectMany(m => m.Genres)
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(spellings => spellings
                .GroupBy(s => s, StringComparer.Ordinal)
                .OrderByDescending(s => s.Count())
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .First().Key)
            .Order(StringComparer.CurrentCultureIgnoreCase);

    private static int? DecadeOf(Movie movie) => movie.Year / 10 * 10;

    private static IEnumerable<Movie> Sort(IEnumerable<Movie> movies, MovieSort sort) => sort switch
    {
        MovieSort.Title => movies.OrderBy(SortTitle, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Year),
        MovieSort.Year => movies.OrderBy(m => m.Year is null).ThenByDescending(m => m.Year).ThenBy(SortTitle, StringComparer.CurrentCultureIgnoreCase),
        _ => movies.OrderByDescending(m => m.LastAddedAt).ThenBy(SortTitle, StringComparer.CurrentCultureIgnoreCase),
    };

    private static string SortTitle(Movie movie)
    {
        var article = LeadingArticles.FirstOrDefault(a => movie.Title.StartsWith(a, StringComparison.OrdinalIgnoreCase));
        return article is null ? movie.Title : movie.Title[article.Length..];
    }
}
