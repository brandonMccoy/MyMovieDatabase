using System.Globalization;
using MMDb.Domain.Comics;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Comics;

/// <summary>Applies a <see cref="ComicQuery"/> in memory, ignoring case and accents (see <see cref="Movies.MovieFilter"/>).</summary>
internal static class ComicFilter
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <param name="wanted">The comics on the wishlist: a run's missing issues that are wanted say so.</param>
    public static ComicShelf Apply(IReadOnlyList<Comic> all, IReadOnlyList<ComicSeries> series, ComicQuery query, IReadOnlyList<WishlistComic> wanted)
    {
        var singles = all.Where(c => !c.Format.IsCollected()).ToList();
        var facets = new ComicFacets(
            IssueCount: singles.Count,
            SeriesCount: singles.Select(RunKey).Distinct().Count(),
            CollectedCount: all.Count(c => c.Format.IsCollected()),
            KeyIssueCount: all.Count(c => c.IsKeyIssue),
            Publishers: [.. all.Select(c => c.Publisher).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)]);

        var terms = (query.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matching = all
            .Where(c => terms.All(term => Matches(c, term)))
            .Where(c => query.Publisher is null || string.Equals(c.Publisher, query.Publisher, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return query.View switch
        {
            ComicView.Series => new ComicShelf([.. Runs(matching.Where(c => !c.Format.IsCollected()), series, wanted, query.Sort, showMissing: terms.Length == 0)], [], facets),
            ComicView.Issues => new ComicShelf([], [.. Sort(matching.Where(c => !c.Format.IsCollected()), query.Sort)], facets),
            ComicView.Collected => new ComicShelf([], [.. Sort(matching.Where(c => c.Format.IsCollected()), query.Sort)], facets),
            _ => new ComicShelf([], [.. Sort(matching.Where(c => c.IsKeyIssue), query.Sort)], facets),
        };
    }

    private static (string, int?) RunKey(Comic comic) => RunKey(comic.Series, comic.SeriesYear);

    /// <summary>What makes single issues one run: the series' name, whatever its case, and the year it began.</summary>
    public static (string, int?) RunKey(string? series, int? seriesYear) => ((series ?? "").ToUpperInvariant(), seriesYear);

    /// <summary>The Metron series a run is: the one most of its copies are linked to.</summary>
    public static int? SeriesOf(IEnumerable<int?> metronSeriesIds) =>
        metronSeriesIds.OfType<int>().GroupBy(id => id).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();

    // While searching, only what matched is shown: a run's gaps would be every issue that didn't.
    private static IEnumerable<SeriesRun> Runs(IEnumerable<Comic> singles, IReadOnlyList<ComicSeries> known, IReadOnlyList<WishlistComic> wanted, ComicSort sort, bool showMissing)
    {
        var wantedIssues = wanted.Where(w => !w.Format.IsCollected()).ToList();
        var byMetronId = known.ToDictionary(s => s.MetronId);
        var runs = singles.GroupBy(RunKey).Select(group =>
        {
            var copies = group.ToList();
            var metronId = SeriesOf(copies.Select(c => c.MetronSeriesId));
            var facts = metronId is { } id ? byMetronId.GetValueOrDefault(id) : null;
            return (Run: Run(copies, facts, metronId, wantedIssues, showMissing), Latest: copies.Max(c => c.CreatedAt));
        });

        return sort == ComicSort.RecentlyAdded
            ? runs.OrderByDescending(r => r.Latest).Select(r => r.Run)
            : runs.Select(r => r.Run).OrderBy(r => SortName(r.Name), Names).ThenBy(r => r.Year);
    }

    private static SeriesRun Run(List<Comic> copies, ComicSeries? facts, int? metronId, List<WishlistComic> wanted, bool showMissing)
    {
        // The spelling most copies use names the run.
        var name = copies.GroupBy(c => c.Series!, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        var owned = copies.GroupBy(c => c.Number!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Comic>)[.. g.OrderBy(c => c.Printing ?? 1).ThenBy(c => c.Variant ?? "", StringComparer.CurrentCultureIgnoreCase)], StringComparer.OrdinalIgnoreCase);

        var entries = owned.Select(o => new RunEntry(o.Key, o.Value)).ToList();
        int? knownCount = null;
        var ownedCount = owned.Count;
        if (facts is { IssueNumbers.Count: > 0 })
        {
            var numbers = facts.IssueNumbers;
            var positions = numbers.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
            var ownedPositions = owned.Keys.Where(positions.ContainsKey).Select(n => positions[n]).ToList();
            if (showMissing && ownedPositions.Count > 0)
            {
                for (var i = ownedPositions.Min(); i <= ownedPositions.Max(); i++)
                {
                    if (!owned.ContainsKey(numbers[i]))
                    {
                        entries.Add(new RunEntry(numbers[i], [], Wanted: wanted.Exists(w => w.IsIssue(metronId, name, copies[0].SeriesYear, numbers[i]))));
                    }
                }
            }

            knownCount = numbers.Count;
            ownedCount = ownedPositions.Count;
        }

        return new SeriesRun(
            Name: name,
            Year: copies[0].SeriesYear,
            Publisher: copies.Select(c => c.Publisher).OfType<string>().FirstOrDefault() ?? facts?.Publisher,
            MetronSeriesId: metronId,
            Entries: Collapse(entries.OrderBy(e => e.Number, IssueNumber.Comparer)),
            OwnedCount: ownedCount,
            KnownCount: knownCount);
    }

    // Missing issues next to each other become one entry: "#159–167". Where the wishlist starts or stops, so does the entry.
    private static List<RunEntry> Collapse(IEnumerable<RunEntry> entries)
    {
        var collapsed = new List<RunEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsMissing && collapsed is [.., { IsMissing: true } previous] && previous.Wanted == entry.Wanted)
            {
                collapsed[^1] = previous with { Through = entry.Number };
            }
            else
            {
                collapsed.Add(entry);
            }
        }

        return collapsed;
    }

    private static IEnumerable<Comic> Sort(IEnumerable<Comic> comics, ComicSort sort) => sort == ComicSort.RecentlyAdded
        ? comics.OrderByDescending(c => c.CreatedAt)
        : comics
            .OrderBy(c => SortName(c.Series ?? c.Title ?? ""), Names)
            .ThenBy(c => c.SeriesYear)
            .ThenBy(c => c.Number, IssueNumber.Comparer)
            .ThenBy(c => c.Title ?? "", Names)
            .ThenBy(c => c.Variant ?? "", Names);

    private static bool Matches(Comic comic, string term) =>
        Contains(comic.Series, term)
        || Contains(comic.Title, term)
        || Contains(comic.Variant, term)
        || Contains(comic.Publisher, term)
        || Contains(comic.KeyNote, term)
        || comic.Writers.Any(w => Contains(w, term))
        || comic.Artists.Any(a => Contains(a, term))
        || string.Equals(comic.Number, IssueNumber.Normalize(term), StringComparison.OrdinalIgnoreCase)
        || comic.SeriesYear?.ToString(CultureInfo.InvariantCulture) == term;

    private static bool Contains(string? source, string term) => source is not null && Compare.IndexOf(source, term, Loose) >= 0;

    private static string SortName(string name) => name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
}
