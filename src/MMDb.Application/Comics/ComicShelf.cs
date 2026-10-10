using System.Globalization;
using MMDb.Domain.Comics;

namespace MMDb.Application.Comics;

/// <summary>What the comics library is showing: search text, which shelf, publisher and sort.</summary>
public sealed record ComicQuery
{
    /// <summary>Words that must each appear in the series, title, number, variant, a creator, the publisher or the key note.</summary>
    public string? Search { get; init; }

    public ComicView View { get; init; } = ComicView.Series;

    public string? Publisher { get; init; }

    public ComicSort Sort { get; init; } = ComicSort.Series;
}

public enum ComicView
{
    /// <summary>Single issues grouped into runs, with the issues missing from each run.</summary>
    Series,

    /// <summary>Every single issue, one tile per copy.</summary>
    Issues,

    /// <summary>Trades, hardcovers and omnibuses.</summary>
    Collected,

    KeyIssues,
}

public enum ComicSort
{
    /// <summary>Series A–Z (ignoring a leading "The"), then the year it began, then issue number.</summary>
    Series,

    /// <summary>Newest first.</summary>
    RecentlyAdded,
}

/// <summary>The comics matching a <see cref="ComicQuery"/>: runs for <see cref="ComicView.Series"/>, copies for the other views.</summary>
public sealed record ComicShelf(IReadOnlyList<SeriesRun> Runs, IReadOnlyList<Comic> Comics, ComicFacets Facets);

/// <summary>A series' single issues, as owned and as missing.</summary>
/// <param name="Entries">Owned issues, and the issues missing between the first and last owned, in running order; missing issues next to each other share one entry.</param>
/// <param name="OwnedCount">Issue numbers owned (variants count once).</param>
/// <param name="KnownCount">Issues in the series according to Metron; <see langword="null"/> if it isn't known.</param>
public sealed record SeriesRun(
    string Name,
    int? Year,
    string? Publisher,
    int? MetronSeriesId,
    IReadOnlyList<RunEntry> Entries,
    int OwnedCount,
    int? KnownCount)
{
    /// <summary>"Daredevil (1964)".</summary>
    public string Title => Year is { } year ? $"{Name} ({year.ToString(CultureInfo.InvariantCulture)})" : Name;
}

/// <param name="Copies">Every copy owned of this issue (variants, printings); empty if it is missing.</param>
/// <param name="Through">For missing issues, the last of a range that starts at <paramref name="Number"/>; <see langword="null"/> for one issue.</param>
public sealed record RunEntry(string Number, IReadOnlyList<Comic> Copies, string? Through = null)
{
    public bool IsMissing => Copies.Count == 0;
}

/// <param name="IssueCount">Single-issue copies in the whole collection.</param>
/// <param name="SeriesCount">Runs of single issues in the whole collection.</param>
/// <param name="CollectedCount">Trades, hardcovers and omnibuses in the whole collection.</param>
/// <param name="KeyIssueCount">Key issues in the whole collection.</param>
/// <param name="Publishers">Every publisher in the collection, A–Z.</param>
public sealed record ComicFacets(int IssueCount, int SeriesCount, int CollectedCount, int KeyIssueCount, IReadOnlyList<string> Publishers);
