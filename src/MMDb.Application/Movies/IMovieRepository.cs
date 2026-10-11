using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Port: where movies are stored. One instance is one unit of work.</summary>
public interface IMovieRepository
{
    Task<Movie?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Read-only: every film as lists show it, without its credits.</summary>
    Task<IReadOnlyList<MovieListing>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only: every film in full. Only for writing them all out (a vault sync).</summary>
    Task<IReadOnlyList<Movie>> ListInFullAsync(CancellationToken cancellationToken = default);

    /// <summary>The films with someone in the cast or crew whose name contains this, ignoring case and accents.</summary>
    Task<IReadOnlySet<Guid>> FindCreditingAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The film owning a format with this barcode, in either of its forms (UPC-A or EAN-13).</summary>
    Task<Movie?> FindByUpcAsync(Upc upc, CancellationToken cancellationToken = default);

    Task<Movie?> FindByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default);

    /// <summary>Read-only: the films linked to any of these TMDB ids.</summary>
    Task<IReadOnlyList<MovieListing>> ListByTmdbIdsAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken = default);

    /// <summary>Read-only: the films owned digitally, each with its video file if it has one.</summary>
    Task<IReadOnlyList<DigitalCopy>> ListDigitalAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only: every film as the Stats screen counts it.</summary>
    Task<IReadOnlyList<FilmTally>> ListTalliesAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only: the first <paramref name="top"/> names of every film's cast, without the rest of its credits.</summary>
    Task<IReadOnlyList<BilledName>> ListTopBilledAsync(int top, CancellationToken cancellationToken = default);

    void Add(Movie movie);

    void Remove(Movie movie);

    /// <exception cref="ConcurrencyConflictException">The movie was changed or deleted by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the Stats screen counts of a film.</summary>
/// <param name="CreatedAt">When the film joined the collection.</param>
public sealed record FilmTally(int? Year, IReadOnlyList<string> Directors, DateTimeOffset CreatedAt, IReadOnlyList<FormatTally> Formats);

public sealed record FormatTally(MediaFormat Format, DateTimeOffset AddedAt);

/// <summary>Someone among the first names of a film's cast.</summary>
public sealed record BilledName(Guid MovieId, string Name);

/// <summary>A film's digital copy: what the folder import needs to know of it.</summary>
/// <param name="FilePath">The video file on the PC, if one is linked.</param>
/// <param name="Formats">Every format the film is owned on, Digital included.</param>
public sealed record DigitalCopy(
    Guid MovieId, string Title, int? Year, string? FilePath, IReadOnlyList<MediaFormat> Formats, int? TmdbId = null, string? CoverImage = null);

/// <summary>A save lost the race with another change to the same product.</summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
