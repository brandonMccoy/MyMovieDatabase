using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Outcome of adding or updating a movie.</summary>
public abstract record SaveMovieResult
{
    private SaveMovieResult()
    {
    }

    public sealed record Saved(Movie Movie) : SaveMovieResult;

    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : SaveMovieResult;

    public sealed record NotFound : SaveMovieResult;

    /// <summary>A film with the same TMDB id is already owned on every format being added.</summary>
    public sealed record AlreadyOwned(Movie Movie) : SaveMovieResult;

    /// <summary>Someone else changed the movie since the caller loaded it.</summary>
    public sealed record Conflict : SaveMovieResult;
}
