using MMDb.Domain;

namespace MMDb.Application.Movies;

/// <summary>What the movie library is showing: search text, filters and sort.</summary>
public sealed record MovieQuery
{
    /// <summary>Words that must each appear in the title, a director, a cast member or the year.</summary>
    public string? Search { get; init; }

    /// <summary>Only films owned on one of these; none means any format.</summary>
    public IReadOnlyList<MediaFormat> Formats { get; init; } = [];

    /// <summary>Only films in one of these genres, ignoring case; none means any genre.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Only films from one of these decades, each its first year, e.g. 1990; none means any decade.</summary>
    public IReadOnlyList<int> Decades { get; init; } = [];

    /// <summary>Only films this person directed: the whole name, ignoring case and accents.</summary>
    public string? Director { get; init; }

    /// <summary>Only films with this person among the top-billed cast: the whole name, ignoring case and accents.</summary>
    public string? Actor { get; init; }

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
