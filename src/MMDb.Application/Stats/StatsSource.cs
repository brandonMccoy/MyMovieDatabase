using System.Globalization;
using MMDb.Application.Comics;
using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Application.Stats;

/// <summary>
/// Counts the collection for the Stats screen. It reads a tally of each film and comic (the few columns that
/// are counted, never the summaries, and of the credits only each film's top-billed cast) and adds them up in memory.
/// </summary>
public sealed class StatsSource(IMovieRepository movies, IComicRepository comics, IWishlistRepository wishlist, TimeProvider time)
{
    private const int MonthsShown = 12;
    private const int DirectorsShown = 6;
    private const int ActorsShown = 6;
    private const int PublishersShown = 6;

    // The library's director and actor filters tell names apart the same way, so a bar and the list it opens agree.
    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    public async Task<CollectionStats> ReadAsync(CancellationToken cancellationToken = default)
    {
        var films = await movies.ListTalliesAsync(cancellationToken);
        var billed = await movies.ListTopBilledAsync(MovieFilter.TopBilled, cancellationToken);
        var copies = await comics.ListTalliesAsync(cancellationToken);
        var series = await comics.ListSeriesAsync(cancellationToken);
        var (wantedMovies, wantedComics) = await wishlist.CountAsync(cancellationToken);

        var zone = time.LocalTimeZone;
        var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        var thisMonth = MonthOf(now);
        (int Year, int Month) MonthIn(DateTimeOffset at) => MonthOf(TimeZoneInfo.ConvertTime(at, zone));

        var formats = films.SelectMany(f => f.Formats).ToList();
        var issues = copies.Where(c => !c.Format.IsCollected()).ToList();
        var runs = issues.GroupBy(c => ComicFilter.RunKey(c.Series, c.SeriesYear)).ToList();
        var knownSeries = series.ToDictionary(s => s.MetronId);

        var months = Enumerable.Range(0, MonthsShown).Select(back => MonthOf(now.AddMonths(back - (MonthsShown - 1)))).ToList();
        IReadOnlyList<MonthCount> ByMonth(IEnumerable<DateTimeOffset> added)
        {
            var counts = added.Select(MonthIn).CountBy(month => month).ToDictionary();
            return [.. months.Select(month => new MonthCount(month.Year, month.Month, counts.GetValueOrDefault(month)))];
        }

        return new CollectionStats(
            FilmCount: films.Count,
            FilmsAddedThisMonth: films.Count(f => MonthIn(f.CreatedAt) == thisMonth),
            DiscCount: formats.Count(f => f.Format.IsDisc()),
            FilmsOnSeveralFormats: films.Count(f => f.Formats.Count > 1),
            DigitalCount: formats.Count(f => !f.Format.IsDisc()),
            IssueCount: issues.Count,
            IssuesAddedThisMonth: issues.Count(c => MonthIn(c.CreatedAt) == thisMonth),
            CollectedCount: copies.Count - issues.Count,
            SeriesCount: runs.Count,
            CompleteSeriesCount: runs.Count(run => IsComplete(run, knownSeries)),
            WishlistMovieCount: wantedMovies,
            WishlistComicCount: wantedComics,
            Formats: [.. Enum.GetValues<MediaFormat>().Select(format => new FormatCount(format, formats.Count(f => f.Format == format)))],
            MoviesAddedByMonth: ByMonth(formats.Select(f => f.AddedAt)),
            ComicsAddedByMonth: ByMonth(copies.Select(c => c.CreatedAt)),
            Decades: [.. Decades(films)],
            FilmsWithoutYear: films.Count(f => f.Year is null),
            TopDirectors: [.. Top(films.Select(f => f.Directors), DirectorsShown)],
            TopActors: [.. Top(billed.GroupBy(b => b.MovieId, b => b.Name), ActorsShown)],
            Publishers: [.. Publishers(issues)]);
    }

    private static (int Year, int Month) MonthOf(DateTimeOffset at) => (at.Year, at.Month);

    private static bool IsComplete(IEnumerable<ComicTally> run, Dictionary<int, ComicSeries> known)
    {
        var copies = run.ToList();
        if (ComicFilter.SeriesOf(copies.Select(c => c.MetronSeriesId)) is not { } id || !known.TryGetValue(id, out var facts) || facts.IssueNumbers.Count == 0)
        {
            return false;
        }

        var owned = copies.Select(c => c.Number).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return facts.IssueNumbers.All(owned.Contains);
    }

    private static IEnumerable<DecadeCount> Decades(IReadOnlyList<FilmTally> films)
    {
        var counts = films.Select(f => f.Year / 10 * 10).OfType<int>().CountBy(decade => decade).ToDictionary();
        if (counts.Count == 0)
        {
            return [];
        }

        var first = counts.Keys.Min();
        return Enumerable.Range(0, (counts.Keys.Max() - first) / 10 + 1)
            .Select(i => first + i * 10)
            .Select(decade => new DecadeCount(decade, counts.GetValueOrDefault(decade)));
    }

    // The people the most films name; someone a film names twice counts once for it.
    private static IEnumerable<NameCount> Top(IEnumerable<IEnumerable<string>> namesPerFilm, int shown) =>
        namesPerFilm.SelectMany(names => names.Distinct(Names))
            .GroupBy(name => name, Names)
            .Select(spellings => new NameCount(MostUsed(spellings), spellings.Count()))
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name, Names)
            .Take(shown);

    private static IEnumerable<PublisherCount> Publishers(List<ComicTally> issues)
    {
        // The comics library's publisher filter ignores case and nothing else.
        var named = issues.Select(c => c.Publisher).OfType<string>()
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(spellings => new PublisherCount(MostUsed(spellings), spellings.Count()))
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unnamed = issues.Count - named.Sum(p => p.Count);
        var shown = named.Count + (unnamed > 0 ? 1 : 0) <= PublishersShown ? named : named[..(PublishersShown - 1)];
        var rest = issues.Count - shown.Sum(p => p.Count);
        return rest > 0 ? shown.Append(new PublisherCount(null, rest)) : shown;
    }

    // The spelling most of them use (ties: the first in ordinal order), like the library's genre options.
    private static string MostUsed(IEnumerable<string> spellings) =>
        spellings.GroupBy(s => s, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;
}
