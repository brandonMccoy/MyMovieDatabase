using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;

namespace MMDb.Contracts.Wishlist;

/// <param name="Movies">The films matching the search, newest first.</param>
/// <param name="MovieCount">Films on the whole wishlist.</param>
/// <param name="Comics">The comics matching the search, newest first.</param>
/// <param name="ComicCount">Comics on the whole wishlist.</param>
public sealed record WishlistResponse(IReadOnlyList<WishlistMovieDto> Movies, int MovieCount, IReadOnlyList<WishlistComicDto> Comics, int ComicCount);

/// <summary>One film on the wishlist.</summary>
public sealed record WishlistMovieDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    /// <summary>The format wanted.</summary>
    public required MovieFormatKind Format { get; init; }

    /// <summary>The formats the film is owned on already: wanting another one is an upgrade.</summary>
    public IReadOnlyList<MovieFormatKind> OwnedFormats { get; init; } = [];

    /// <summary>The cover kept on the PC, or <see langword="null"/> for a placeholder.</summary>
    public Uri? CoverUrl { get; init; }

    /// <summary>The trailer on YouTube, or a YouTube search for it.</summary>
    public required Uri TrailerUrl { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}

/// <summary>A film to put on the wishlist: a lookup match, or what the add screen holds.</summary>
public sealed record WantMovieRequest
{
    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public int? TmdbId { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    /// <summary>TMDB's poster (from a lookup); the PC downloads it when saving.</summary>
    public string? PosterPath { get; init; }

    public required MovieFormatKind Format { get; init; }
}

/// <summary>The format a film on the wishlist is now wanted on.</summary>
public sealed record ChangeWishFormatRequest(MovieFormatKind Format);

/// <param name="Status">
/// <see cref="SaveStatus.AlreadyOwned"/>: the film is in the collection on that format (<paramref name="Owned"/>).
/// <see cref="SaveStatus.NotFound"/>: it isn't on the wishlist (any more).
/// </param>
/// <param name="Errors">Per field: <c>Title</c>, <c>Year</c>, <c>PosterPath</c>, <c>Format</c>.</param>
public sealed record WantMovieResponse(SaveStatus Status, WishlistMovieDto? Movie = null, MovieSummaryDto? Owned = null, IReadOnlyList<FieldError>? Errors = null)
{
    public IReadOnlyList<FieldError> Errors { get; init; } = Errors ?? [];
}

/// <summary>One comic on the wishlist: an issue or a collected edition.</summary>
public sealed record WishlistComicDto
{
    public required Guid Id { get; init; }

    /// <summary>"Saga #7", "Saga Vol. 1" or a collected edition's title.</summary>
    public required string DisplayName { get; init; }

    public required ComicFormatKind Format { get; init; }

    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    /// <summary>The issue number, or a collected edition's volume number.</summary>
    public string? Number { get; init; }

    public string? Publisher { get; init; }

    /// <summary>Added from a series' missing issues in the library.</summary>
    public bool FromMissing { get; init; }

    /// <summary>The cover kept on the PC, or <see langword="null"/> for a placeholder.</summary>
    public Uri? CoverUrl { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}

/// <summary>An issue or a collected edition to put on the wishlist: a lookup match, or what the add screen holds.</summary>
public sealed record WantComicRequest
{
    public ComicFormatKind Format { get; init; }

    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    /// <summary>The issue number, or a collected edition's volume number.</summary>
    public string? Number { get; init; }

    public string? Title { get; init; }

    public string? Publisher { get; init; }

    public string? Isbn { get; init; }

    public int? MetronId { get; init; }

    public int? MetronSeriesId { get; init; }

    /// <summary>Where the cover image is online; the PC downloads it when saving.</summary>
    public string? CoverSource { get; init; }
}

/// <summary>A run of issues missing from a series, to put on the wishlist: each issue as an item of its own.</summary>
/// <param name="MetronSeriesId">The series (see <see cref="SeriesRunDto.MetronSeriesId"/>).</param>
/// <param name="From">The first missing issue (see <see cref="RunEntryDto.Number"/>).</param>
/// <param name="Through">The last one (see <see cref="RunEntryDto.Through"/>); <see langword="null"/> for a single issue.</param>
public sealed record WantMissingRequest(int MetronSeriesId, string From, string? Through = null);

/// <param name="Status">
/// <see cref="SaveStatus.AlreadyOwned"/>: a copy of the issue is in the collection (<paramref name="Owned"/>).
/// <see cref="SaveStatus.NotFound"/>: the series or its issues aren't known.
/// </param>
/// <param name="Comic">The item wanted; for a run of missing issues, the first of them.</param>
public sealed record WantComicResponse(SaveStatus Status, WishlistComicDto? Comic = null, ComicSummaryDto? Owned = null, IReadOnlyList<FieldError>? Errors = null)
{
    public IReadOnlyList<FieldError> Errors { get; init; } = Errors ?? [];
}

/// <summary>That a comic found online is on the wishlist, and what the wishlist calls it.</summary>
/// <param name="Number">The issue number, or a collected edition's volume number.</param>
public sealed record ComicWishDto(Guid Id, string DisplayName, string? Series = null, int? SeriesYear = null, string? Number = null);

/// <summary>That a film found online is on the wishlist, on which format it is wanted, and what the wishlist calls it.</summary>
public sealed record MovieWishDto(Guid Id, MovieFormatKind Format, string Title, int? Year = null);
