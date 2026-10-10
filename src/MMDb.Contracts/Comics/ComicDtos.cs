using MMDb.Contracts.Movies;

namespace MMDb.Contracts.Comics;

/// <summary>What kind of comic a copy is. Declared in display order.</summary>
public enum ComicFormatKind
{
    SingleIssue,
    TradePaperback,
    Hardcover,
    Omnibus,
}

/// <summary>Which shelf the comics library shows.</summary>
public enum ComicShelfKind
{
    /// <summary>Single issues grouped into runs, with missing issues.</summary>
    Series,

    Issues,

    /// <summary>Trades, hardcovers and omnibuses.</summary>
    Collected,

    KeyIssues,
}

public enum ComicSortOrder
{
    /// <summary>Series A–Z, then the year it began, then issue number.</summary>
    Series,

    RecentlyAdded,
}

/// <summary>One comic copy, everything the detail and edit screens show.</summary>
public sealed record ComicDto
{
    public required Guid Id { get; init; }

    /// <summary>Send back when saving changes, to detect edits made elsewhere.</summary>
    public required int Version { get; init; }

    /// <summary>"Daredevil #181", "Saga Vol. 1" or a collected edition's title.</summary>
    public required string DisplayName { get; init; }

    public required ComicFormatKind Format { get; init; }

    public string? Series { get; init; }

    /// <summary>The year the series began: Daredevil (1964).</summary>
    public int? SeriesYear { get; init; }

    public string? Number { get; init; }

    public string? Title { get; init; }

    public string? Variant { get; init; }

    public int? Printing { get; init; }

    public string? Publisher { get; init; }

    public DateOnly? CoverDate { get; init; }

    public IReadOnlyList<string> Writers { get; init; } = [];

    public IReadOnlyList<string> Artists { get; init; } = [];

    public string? Summary { get; init; }

    /// <summary>The cover barcode and its add-on as one run of digits.</summary>
    public string? Barcode { get; init; }

    public string? Isbn { get; init; }

    public int? MetronId { get; init; }

    public int? MetronSeriesId { get; init; }

    public bool IsKeyIssue { get; init; }

    public string? KeyNote { get; init; }

    /// <summary>Where the cover came from online; kept so a save can fetch it again.</summary>
    public string? CoverSource { get; init; }

    /// <summary>The cover stored on the PC, if one has been downloaded.</summary>
    public Uri? CoverUrl { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The run this single issue belongs to; <see langword="null"/> for a collected edition.</summary>
    public SeriesRunDto? Run { get; init; }
}

/// <summary>A copy as a tile in the library.</summary>
public sealed record ComicSummaryDto
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required ComicFormatKind Format { get; init; }

    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    public string? Number { get; init; }

    public string? Title { get; init; }

    public string? Variant { get; init; }

    public int? Printing { get; init; }

    public string? Publisher { get; init; }

    public DateOnly? CoverDate { get; init; }

    public bool IsKeyIssue { get; init; }

    public string? KeyNote { get; init; }

    public Uri? CoverUrl { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <param name="Entries">Issues owned, and missing between the first and last owned, in running order; missing issues next to each other share one entry.</param>
/// <param name="OwnedCount">Issue numbers owned (variants count once).</param>
/// <param name="KnownCount">Issues in the series according to Metron, if known.</param>
public sealed record SeriesRunDto(
    string Name,
    int? Year,
    string Title,
    string? Publisher,
    IReadOnlyList<RunEntryDto> Entries,
    int OwnedCount,
    int? KnownCount);

/// <param name="Copies">Every copy owned of the issue; empty if it is missing.</param>
/// <param name="Through">For missing issues, the last of a range that starts at <paramref name="Number"/>; <see langword="null"/> for one issue.</param>
public sealed record RunEntryDto(string Number, IReadOnlyList<ComicSummaryDto> Copies, string? Through = null)
{
    public bool IsMissing => Copies.Count == 0;
}

public sealed record ComicListRequest
{
    public string? Search { get; init; }

    public ComicShelfKind Shelf { get; init; } = ComicShelfKind.Series;

    public string? Publisher { get; init; }

    public ComicSortOrder Sort { get; init; } = ComicSortOrder.Series;
}

/// <param name="Runs">For <see cref="ComicShelfKind.Series"/>.</param>
/// <param name="Comics">For the other shelves.</param>
public sealed record ComicListResponse(IReadOnlyList<SeriesRunDto> Runs, IReadOnlyList<ComicSummaryDto> Comics, ComicFacetsDto Facets);

/// <summary>Counts for the whole collection (search and filters left out), and the publisher filter's options.</summary>
public sealed record ComicFacetsDto(int IssueCount, int SeriesCount, int CollectedCount, int KeyIssueCount, IReadOnlyList<string> Publishers);

/// <summary>The editable fields of a copy.</summary>
public sealed record SaveComicRequest
{
    public ComicFormatKind Format { get; init; } = ComicFormatKind.SingleIssue;

    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    public string? Number { get; init; }

    public string? Title { get; init; }

    public string? Variant { get; init; }

    public int? Printing { get; init; }

    public string? Publisher { get; init; }

    public DateOnly? CoverDate { get; init; }

    public IReadOnlyList<string> Writers { get; init; } = [];

    public IReadOnlyList<string> Artists { get; init; } = [];

    public string? Summary { get; init; }

    /// <summary>As typed or scanned: the UPC and its add-on.</summary>
    public string? Barcode { get; init; }

    public string? Isbn { get; init; }

    public int? MetronId { get; init; }

    public int? MetronSeriesId { get; init; }

    public bool IsKeyIssue { get; init; }

    public string? KeyNote { get; init; }

    /// <summary>The cover's link online (from a lookup match), downloaded when the copy is saved.</summary>
    public string? CoverSource { get; init; }
}

/// <param name="Comic">The copy saved; for <see cref="SaveStatus.AlreadyOwned"/>, the copy already in the collection.</param>
/// <param name="Errors">
/// For <see cref="SaveStatus.Invalid"/>: <c>Format</c>, <c>Series</c>, <c>SeriesYear</c>, <c>Number</c>, <c>Title</c>, <c>Variant</c>,
/// <c>Printing</c>, <c>Publisher</c>, <c>Writers</c>, <c>Artists</c>, <c>Barcode</c>, <c>Isbn</c>, <c>KeyNote</c> or <c>CoverUrl</c>.
/// </param>
public sealed record SaveComicResponse(SaveStatus Status, ComicDto? Comic = null, IReadOnlyList<FieldError>? Errors = null)
{
    public IReadOnlyList<FieldError> Errors { get; init; } = Errors ?? [];
}

/// <summary>What a search for a comic found online. Nothing has been saved.</summary>
public sealed record ComicLookupResponse
{
    public required LookupStatus Status { get; init; }

    /// <summary>Best match first; each variant cover is a candidate of its own.</summary>
    public IReadOnlyList<ComicCandidateDto> Candidates { get; init; } = [];

    /// <summary>The barcode or ISBN searched for, if one was entered.</summary>
    public string? Barcode { get; init; }

    /// <summary>The copy in the collection with this very barcode or ISBN.</summary>
    public ComicSummaryDto? OwnedByCode { get; init; }

    /// <summary>For the failure statuses: "Metron" or "Open Library".</summary>
    public string? FailedService { get; init; }
}

/// <summary>A comic found online: everything the add screen fills in for review.</summary>
public sealed record ComicCandidateDto
{
    public required SaveComicRequest Details { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The cover online (needs internet).</summary>
    public Uri? CoverUrl { get; init; }

    /// <summary>Copies in the collection of the same issue (any variant).</summary>
    public IReadOnlyList<ComicSummaryDto> Owned { get; init; } = [];

    /// <summary>This very copy (same cover and printing) is already in the collection.</summary>
    public bool IsOwned { get; init; }
}
