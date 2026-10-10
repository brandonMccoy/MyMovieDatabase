namespace MMDb.Contracts.Movies;

public enum LookupStatus
{
    Found,
    NoMatches,

    /// <summary>The barcode database doesn't know the UPC.</summary>
    UnknownBarcode,

    /// <summary>A film in the collection already has this UPC; see <see cref="MovieLookupResponse.OwnedByBarcode"/>.</summary>
    BarcodeOwned,

    /// <summary>No TMDB key is set up on the PC.</summary>
    NotConfigured,

    /// <summary>The online database turned the PC's key down.</summary>
    KeyRejected,

    /// <summary>Too many lookups for now; try again later.</summary>
    Busy,

    /// <summary>Offline, or the online database failed.</summary>
    Unavailable,
}

/// <summary>What a search for a title or UPC found online. Nothing has been saved.</summary>
public sealed record MovieLookupResponse
{
    public required LookupStatus Status { get; init; }

    /// <summary>Best match first.</summary>
    public IReadOnlyList<MovieCandidateDto> Candidates { get; init; } = [];

    /// <summary>The barcode searched for, if a UPC was entered.</summary>
    public string? Upc { get; init; }

    /// <summary>The barcode database's name for the product, e.g. "Heat [Blu-ray]".</summary>
    public string? ProductName { get; init; }

    /// <summary>The film's title as read from the product name, e.g. "Heat": what to search on with.</summary>
    public string? ProductTitle { get; init; }

    /// <summary>The format the product name gives, if any.</summary>
    public MovieFormatKind? Format { get; init; }

    public MovieSummaryDto? OwnedByBarcode { get; init; }

    /// <summary>For the failure statuses: which online database it was, "TMDB" or "UPCitemdb".</summary>
    public string? FailedService { get; init; }
}

/// <summary>A film found online: everything the add screen fills in for review.</summary>
public sealed record MovieCandidateDto
{
    public required int TmdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<CastMemberDto> Cast { get; init; } = [];

    public IReadOnlyList<CrewMemberDto> Crew { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public int? RuntimeMinutes { get; init; }

    public string? Rating { get; init; }

    public string? Overview { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    public string? PosterPath { get; init; }

    /// <summary>The poster on TMDB, for the editor (needs internet).</summary>
    public Uri? PosterUrl { get; init; }

    /// <summary>A small poster on TMDB, for the list of matches.</summary>
    public Uri? ThumbnailUrl { get; init; }

    /// <summary>The film in the collection linked to the same TMDB entry, if there is one.</summary>
    public MovieSummaryDto? Owned { get; init; }
}
