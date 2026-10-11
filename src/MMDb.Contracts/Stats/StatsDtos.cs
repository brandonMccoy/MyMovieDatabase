using MMDb.Contracts.Movies;

namespace MMDb.Contracts.Stats;

/// <summary>The numbers on the Stats screen, for the whole collection.</summary>
public sealed record StatsDto
{
    public int FilmCount { get; init; }

    /// <summary>Films that joined the collection in the current calendar month, by the PC's clock.</summary>
    public int FilmsAddedThisMonth { get; init; }

    public int DiscCount { get; init; }

    /// <summary>Films owned on two formats or more.</summary>
    public int FilmsOnSeveralFormats { get; init; }

    public int DigitalCount { get; init; }

    /// <summary>Single-issue copies; trades, hardcovers and omnibuses aren't counted.</summary>
    public int IssueCount { get; init; }

    public int IssuesAddedThisMonth { get; init; }

    /// <summary>Trades, hardcovers and omnibuses.</summary>
    public int CollectedCount { get; init; }

    public int SeriesCount { get; init; }

    /// <summary>Runs with every issue Metron lists for the series.</summary>
    public int CompleteSeriesCount { get; init; }

    public int WishlistMovieCount { get; init; }

    public int WishlistComicCount { get; init; }

    /// <summary>Copies per format, all four in display order.</summary>
    public IReadOnlyList<FormatCountDto> Formats { get; init; } = [];

    /// <summary>Copies added in each of the last twelve months, oldest first: a film's formats and comic copies.</summary>
    public IReadOnlyList<MonthCountDto> AddedByMonth { get; init; } = [];

    /// <summary>Films per decade of release, oldest first, with the empty decades in between.</summary>
    public IReadOnlyList<DecadeCountDto> Decades { get; init; } = [];

    public int FilmsWithoutYear { get; init; }

    /// <summary>The directors with the most films, most first.</summary>
    public IReadOnlyList<NameCountDto> TopDirectors { get; init; } = [];

    /// <summary>Single issues per publisher, most first; at most six rows.</summary>
    public IReadOnlyList<PublisherCountDto> Publishers { get; init; } = [];
}

public sealed record FormatCountDto(MovieFormatKind Format, int Count);

public sealed record MonthCountDto(int Year, int Month, int Count);

/// <param name="Decade">First year of the decade, e.g. 1990.</param>
public sealed record DecadeCountDto(int Decade, int Count);

public sealed record NameCountDto(string Name, int Count);

/// <param name="Name"><see langword="null"/> for the rest: the smaller publishers together, and issues without one.</param>
public sealed record PublisherCountDto(string? Name, int Count);
