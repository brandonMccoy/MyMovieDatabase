using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Port: where movies are stored. One instance is one unit of work.</summary>
public interface IMovieRepository
{
    Task<Movie?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Movie>> ListAsync(CancellationToken cancellationToken = default);

    void Add(Movie movie);

    void Remove(Movie movie);

    /// <exception cref="ConcurrencyConflictException">The movie was changed or deleted by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A save lost the race with another change to the same product.</summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
