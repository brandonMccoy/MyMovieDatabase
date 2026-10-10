using MMDb.Contracts.Movies;

namespace MMDb.Contracts.Digital;

public enum MovieFolderState
{
    /// <summary>No movie folder has been chosen.</summary>
    NotSetUp,

    Ready,

    /// <summary>The folder isn't there (renamed, or on a drive that isn't connected).</summary>
    FolderMissing,
}

public enum FileMatch
{
    /// <summary>Not looked up on TMDB yet.</summary>
    Waiting,

    /// <summary>One film fits the file's name and year, or the film was picked by hand.</summary>
    Matched,

    /// <summary>A guess that needs a look.</summary>
    Check,

    NoMatch,
}

/// <summary>A film on TMDB that a file could be.</summary>
/// <param name="PosterPath">TMDB's own name for the poster; goes back to the PC with the film when it is picked.</param>
public sealed record FilmHitDto(int TmdbId, string Title, int? Year, Uri? ThumbnailUrl = null, string? PosterPath = null);

/// <summary>A video file in the movie folder waiting for review.</summary>
public sealed record ImportFileDto
{
    /// <summary>The full path: what the file is known by.</summary>
    public required string Path { get; init; }

    /// <summary>The path inside the movie folder.</summary>
    public required string Name { get; init; }

    /// <summary>The title, and year if any, read from the name: what a search by hand starts from.</summary>
    public required string Reading { get; init; }

    public FileMatch Match { get; init; }

    /// <summary>The film the file would be added as.</summary>
    public FilmHitDto? Film { get; init; }

    /// <summary>What the search found, best first.</summary>
    public IReadOnlyList<FilmHitDto> Hits { get; init; } = [];

    public bool Ticked { get; init; }

    /// <summary>The film in the collection that <see cref="Film"/> is, if it is owned: adding gives it the file.</summary>
    public MovieSummaryDto? Owned { get; init; }

    /// <summary>The other file <see cref="Owned"/> already has. Such a file can't be ticked: it replaces that one, or is skipped.</summary>
    public string? OwnedFile { get; init; }

    /// <summary>The <see cref="Name"/> of a file earlier in the list that is the same film.</summary>
    public string? SameFilmAs { get; init; }
}

/// <param name="Name">The path inside the movie folder.</param>
public sealed record SkippedFileDto(string Path, string Name);

/// <summary>A film whose video file isn't where the collection says any more.</summary>
/// <param name="OnlyCopy">Whether the film is owned on nothing but Digital: removing the copy removes the film.</param>
public sealed record MissingFileDto(Guid MovieId, string Title, int? Year, string Path, bool OnlyCopy, Uri? CoverUrl = null);

public enum ImportStep
{
    /// <summary>Looking files up on TMDB.</summary>
    Matching,

    /// <summary>Adding the ticked files to the collection.</summary>
    Adding,
}

public sealed record ImportProgressDto(ImportStep Step, int Done, int Total);

/// <summary>The movie folder and what is in it, as of the last scan.</summary>
public sealed record MovieFolderDto
{
    public MovieFolderState State { get; init; }

    public string? Folder { get; init; }

    /// <summary>Whether new files in the folder show up for review by themselves.</summary>
    public bool Watch { get; init; }

    public int VideoFileCount { get; init; }

    /// <summary>Video files in the folder that are a film's digital copy already.</summary>
    public int LinkedCount { get; init; }

    public IReadOnlyList<ImportFileDto> Files { get; init; } = [];

    public IReadOnlyList<SkippedFileDto> Skipped { get; init; } = [];

    public IReadOnlyList<MissingFileDto> Missing { get; init; } = [];

    /// <summary>Set while files are being looked up or added.</summary>
    public ImportProgressDto? Progress { get; init; }

    /// <summary>Why the last lookup stopped early: one of the failure statuses.</summary>
    public LookupStatus? Problem { get; init; }

    public string? FailedService { get; init; }
}

/// <param name="Status"><see cref="LookupStatus.Found"/>, <see cref="LookupStatus.NoMatches"/> or one of the failure statuses.</param>
public sealed record FilmSearchResponse(LookupStatus Status, IReadOnlyList<FilmHitDto> Hits, string? FailedService = null);
