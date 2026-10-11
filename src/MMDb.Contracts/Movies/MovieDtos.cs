namespace MMDb.Contracts.Movies;

/// <summary>How a film is owned. Same names as the domain's formats; declared in display order.</summary>
public enum MovieFormatKind
{
    Uhd4K,
    BluRay,
    Dvd,
    Digital,
}

public enum MovieSortOrder
{
    RecentlyAdded,
    Title,
    Year,
}

public sealed record CastMemberDto(string Name, string? Character = null);

/// <summary>Someone behind the camera. A film's crew comes from TMDB in credit order: by department, headline jobs first.</summary>
public sealed record CrewMemberDto(string Name, string Job, string? Department = null);

public sealed record MovieFormatDto(MovieFormatKind Format, string? Upc, string? FilePath, DateTimeOffset AddedAt);

/// <summary>Everything about one film, for the detail and edit screens.</summary>
public sealed record MovieDto
{
    public required Guid Id { get; init; }

    /// <summary>Send back with an update; a newer version on the PC means a conflict.</summary>
    public required int Version { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<CastMemberDto> Cast { get; init; } = [];

    /// <summary>Everyone but the directors; send it back unchanged with an update.</summary>
    public IReadOnlyList<CrewMemberDto> Crew { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public int? RuntimeMinutes { get; init; }

    public string? Rating { get; init; }

    public string? Overview { get; init; }

    public int? TmdbId { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    /// <summary>TMDB's poster; send it back unchanged with an update.</summary>
    public string? PosterPath { get; init; }

    /// <summary>The cover kept on the PC, or <see langword="null"/> for a placeholder.</summary>
    public Uri? CoverUrl { get; init; }

    /// <summary>The trailer on YouTube, or a YouTube search for it.</summary>
    public required Uri TrailerUrl { get; init; }

    public IReadOnlyList<MovieFormatDto> Formats { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One poster in the library grid.</summary>
public sealed record MovieSummaryDto(
    Guid Id,
    string Title,
    int? Year,
    IReadOnlyList<string> Directors,
    IReadOnlyList<MovieFormatKind> Formats,
    Uri? CoverUrl = null);

/// <summary>Search, filters and sort for the library.</summary>
public sealed record MovieListRequest
{
    public string? Search { get; init; }

    public MovieFormatKind? Format { get; init; }

    public string? Genre { get; init; }

    /// <summary>First year of the decade, e.g. 1990.</summary>
    public int? Decade { get; init; }

    /// <summary>Only films this person directed: the whole name, ignoring case and accents.</summary>
    public string? Director { get; init; }

    public MovieSortOrder Sort { get; init; } = MovieSortOrder.RecentlyAdded;
}

public sealed record MovieListResponse(IReadOnlyList<MovieSummaryDto> Movies, MovieFacetsDto Facets);

/// <param name="FilmCount">Films in the whole collection.</param>
/// <param name="DiscCount">Physical copies (all formats but Digital) in the whole collection.</param>
/// <param name="MatchingCount">Films matching search, genre and decade, before the format filter.</param>
/// <param name="FormatCounts">Per format, matching films owned on it.</param>
/// <param name="Genres">Genre filter options, A–Z.</param>
/// <param name="Decades">Decade filter options, newest first.</param>
public sealed record MovieFacetsDto(
    int FilmCount,
    int DiscCount,
    int MatchingCount,
    IReadOnlyDictionary<MovieFormatKind, int> FormatCounts,
    IReadOnlyList<string> Genres,
    IReadOnlyList<int> Decades);

/// <summary>The film as entered on the add/edit screen.</summary>
public sealed record SaveMovieRequest
{
    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<CastMemberDto> Cast { get; init; } = [];

    /// <summary>As the lookup match or the film being edited had it.</summary>
    public IReadOnlyList<CrewMemberDto> Crew { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public int? RuntimeMinutes { get; init; }

    public string? Rating { get; init; }

    public string? Overview { get; init; }

    public int? TmdbId { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    /// <summary>TMDB's poster (from a lookup); the PC downloads it when saving.</summary>
    public string? PosterPath { get; init; }

    /// <summary>Every format owned, at least one. Formats left out of an update are removed.</summary>
    public IReadOnlyList<FormatRequest> Formats { get; init; } = [];
}

public sealed record FormatRequest(MovieFormatKind Format, string? Upc = null, string? FilePath = null);

public enum SaveStatus
{
    Saved,
    Invalid,
    NotFound,

    /// <summary>The film was changed elsewhere since it was loaded.</summary>
    Conflict,

    /// <summary>Adding a film already owned (same TMDB id) on every format given; <see cref="SaveMovieResponse.Movie"/> is that film.</summary>
    AlreadyOwned,
}

/// <param name="Field">
/// Which input is wrong: <c>Title</c>, <c>Year</c>, <c>RuntimeMinutes</c>, <c>Rating</c>, <c>Cast</c>, <c>Formats</c>,
/// <c>PosterPath</c>, or per format <c>{MovieFormatKind}.Upc</c> / <c>{MovieFormatKind}.FilePath</c>.
/// </param>
public sealed record FieldError(string Field, string Message);

public sealed record SaveMovieResponse(SaveStatus Status, MovieDto? Movie = null, IReadOnlyList<FieldError>? Errors = null)
{
    public IReadOnlyList<FieldError> Errors { get; init; } = Errors ?? [];
}
