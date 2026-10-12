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
    /// <summary>
    /// How many of a film's cast, from the top of the bill, count as its actors: for "Top actors" on the Stats
    /// screen and the list an actor's bar opens. Further down are the bit parts.
    /// </summary>
    public const int TopBilled = 10;

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private static readonly string[] LeadingArticles = ["The ", "A ", "An "];

    /// <summary>The words of a search; a film matches when every one of them does.</summary>
    public static string[] Terms(MovieQuery query) =>
        (query.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether two names are one person's: the whole name, ignoring case and accents.</summary>
    public static bool SameName(string a, string b) => Compare.Compare(a, b, Loose) == 0;

    /// <param name="crediting">For each of the <see cref="Terms"/>, the films with a cast or crew member matching it.</param>
    /// <param name="starring">The films with <see cref="MovieQuery.Actor"/> among the top-billed cast; not needed without one.</param>
    public static MovieList Apply(
        IReadOnlyList<MovieListing> all, MovieQuery query, IReadOnlyDictionary<string, IReadOnlySet<Guid>> crediting, IReadOnlySet<Guid>? starring = null)
    {
        var terms = Terms(query);

        var matching = all
            .Where(m => terms.All(term => Matches(m, term) || crediting[term].Contains(m.Id)))
            .Where(m => query.Genres.Count == 0 || m.Genres.Any(g => query.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
            .Where(m => query.Decades.Count == 0 || (DecadeOf(m) is { } decade && query.Decades.Contains(decade)))
            .Where(m => query.Director is null || m.Directors.Any(d => SameName(d, query.Director)))
            .Where(m => query.Actor is null || starring?.Contains(m.Id) == true)
            .ToList();

        var facets = new MovieFacets(
            FilmCount: all.Count,
            DiscCount: all.Sum(m => m.Formats.Count(f => f.IsDisc())),
            MatchingCount: matching.Count,
            FormatCounts: Enum.GetValues<MediaFormat>().ToDictionary(f => f, f => matching.Count(m => m.Owns(f))),
            Genres: [.. GenreOptions(all)],
            Decades: [.. all.Select(DecadeOf).OfType<int>().Distinct().OrderDescending()]);

        var shown = matching.Where(m => query.Formats.Count == 0 || query.Formats.Any(m.Owns));
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
