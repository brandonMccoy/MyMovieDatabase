using MMDb.Domain;

namespace MMDb.Application.Stats;

/// <summary>The numbers on the Stats screen, for the whole collection.</summary>
/// <param name="FilmsAddedThisMonth">Films that joined the collection in the current calendar month, by the PC's clock.</param>
/// <param name="FilmsOnSeveralFormats">Films owned on two formats or more.</param>
/// <param name="IssueCount">Single-issue copies; trades, hardcovers and omnibuses aren't counted.</param>
/// <param name="CollectedCount">Trades, hardcovers and omnibuses.</param>
/// <param name="CompleteSeriesCount">Runs with every issue Metron lists for the series.</param>
/// <param name="Formats">Copies per format, all four in display order.</param>
/// <param name="MoviesAddedByMonth">Film copies (each format of a film) added in each of the last twelve months, oldest first.</param>
/// <param name="ComicsAddedByMonth">Comic copies (issues, trades and hardcovers) added in each of the last twelve months, oldest first.</param>
/// <param name="Decades">Films per decade of release, oldest first, with the empty decades in between.</param>
/// <param name="TopDirectors">The directors with the most films, most first.</param>
/// <param name="TopActors">The actors in the most films, most first, counting only each film's top-billed cast.</param>
/// <param name="Publishers">Single issues per publisher, most first; at most six rows, the last of which may be the rest.</param>
public sealed record CollectionStats(
    int FilmCount,
    int FilmsAddedThisMonth,
    int DiscCount,
    int FilmsOnSeveralFormats,
    int DigitalCount,
    int IssueCount,
    int IssuesAddedThisMonth,
    int CollectedCount,
    int SeriesCount,
    int CompleteSeriesCount,
    int WishlistMovieCount,
    int WishlistComicCount,
    IReadOnlyList<FormatCount> Formats,
    IReadOnlyList<MonthCount> MoviesAddedByMonth,
    IReadOnlyList<MonthCount> ComicsAddedByMonth,
    IReadOnlyList<DecadeCount> Decades,
    int FilmsWithoutYear,
    IReadOnlyList<NameCount> TopDirectors,
    IReadOnlyList<NameCount> TopActors,
    IReadOnlyList<PublisherCount> Publishers);

public sealed record FormatCount(MediaFormat Format, int Count);

public sealed record MonthCount(int Year, int Month, int Count);

/// <param name="Decade">First year of the decade, e.g. 1990.</param>
public sealed record DecadeCount(int Decade, int Count);

public sealed record NameCount(string Name, int Count);

/// <param name="Name"><see langword="null"/> for the rest: the smaller publishers together, and issues without one.</param>
public sealed record PublisherCount(string? Name, int Count);
