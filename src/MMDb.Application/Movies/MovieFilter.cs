using System.Globalization;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>
/// Applies a <see cref="MovieQuery"/> in memory. A personal collection is a few thousand films at most,
/// and matching here (rather than in SQL) makes search ignore case and accents: "amelie" finds "Amélie".
/// The one thing matched in the database is who is in a film's credits: those are too many to load for a list.
/// </summary>
internal static class MovieFilter
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private static readonly string[] LeadingArticles = ["The ", "A ", "An "];

    /// <summary>The words of a search; a film matches when every one of them does.</summary>
    public static string[] Terms(MovieQuery query) =>
        (query.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <param name="crediting">For each of the <see cref="Terms"/>, the films with a cast or crew member matching it.</param>
    public static MovieList Apply(IReadOnlyList<MovieListing> all, MovieQuery query, IReadOnlyDictionary<string, IReadOnlySet<Guid>> crediting)
    {
        var terms = Terms(query);

        var matching = all
            .Where(m => terms.All(term => Matches(m, term) || crediting[term].Contains(m.Id)))
            .Where(m => query.Genre is null || m.Genres.Contains(query.Genre, StringComparer.OrdinalIgnoreCase))
            .Where(m => query.Decade is null || DecadeOf(m) == query.Decade)
            .Where(m => query.Director is null || m.Directors.Any(d => Compare.Compare(d, query.Director, Loose) == 0))
            .ToList();

        var facets = new MovieFacets(
            FilmCount: all.Count,
            DiscCount: all.Sum(m => m.Formats.Count(f => f.IsDisc())),
            MatchingCount: matching.Count,
            FormatCounts: Enum.GetValues<MediaFormat>().ToDictionary(f => f, f => matching.Count(m => m.Owns(f))),
            Genres: [.. GenreOptions(all)],
            Decades: [.. all.Select(DecadeOf).OfType<int>().Distinct().OrderDescending()]);

        var shown = matching.Where(m => query.Format is not { } format || m.Owns(format));
        return new MovieList([.. Sort(shown, query.Sort)], facets);
    }

    private static bool Matches(MovieListing movie, string term) =>
        Contains(movie.Title, term)
        || movie.Directors.Any(d => Contains(d, term))
        || movie.Year?.ToString(CultureInfo.InvariantCulture) == term;

    private static bool Contains(string source, string term) => Compare.IndexOf(source, term, Loose) >= 0;

    // "Crime" and "crime" are one genre; show the most used spelling (ties: the capitalized one).
    private static IEnumerable<string> GenreOptions(IEnumerable<MovieListing> movies) =>
        movies.SelectMany(m => m.Genres)
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(spellings => spellings
                .GroupBy(s => s, StringComparer.Ordinal)
                .OrderByDescending(s => s.Count())
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .First().Key)
            .Order(StringComparer.CurrentCultureIgnoreCase);

    private static int? DecadeOf(MovieListing movie) => movie.Year / 10 * 10;

    private static IEnumerable<MovieListing> Sort(IEnumerable<MovieListing> movies, MovieSort sort) => sort switch
    {
        MovieSort.Title => movies.OrderBy(SortTitle, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Year),
        MovieSort.Year => movies.OrderBy(m => m.Year is null).ThenByDescending(m => m.Year).ThenBy(SortTitle, StringComparer.CurrentCultureIgnoreCase),
        _ => movies.OrderByDescending(m => m.LastAddedAt).ThenBy(SortTitle, StringComparer.CurrentCultureIgnoreCase),
    };

    private static string SortTitle(MovieListing movie)
    {
        var article = LeadingArticles.FirstOrDefault(a => movie.Title.StartsWith(a, StringComparison.OrdinalIgnoreCase));
        return article is null ? movie.Title : movie.Title[article.Length..];
    }
}
