using MMDb.Domain;

namespace MMDb.Application.Movies;

/// <summary>What the movie library is showing: search text, filters and sort.</summary>
public sealed record MovieQuery
{
    /// <summary>Words that must each appear in the title, a director, a cast member or the year.</summary>
    public string? Search { get; init; }

    public MediaFormat? Format { get; init; }

    public string? Genre { get; init; }

    /// <summary>First year of the decade, e.g. 1990.</summary>
    public int? Decade { get; init; }

    public MovieSort Sort { get; init; } = MovieSort.RecentlyAdded;
}

public enum MovieSort
{
    /// <summary>Newest format added first.</summary>
    RecentlyAdded,

    /// <summary>A–Z, ignoring a leading "The", "A" or "An".</summary>
    Title,

    /// <summary>Newest release first; films without a year last.</summary>
    Year,
}
