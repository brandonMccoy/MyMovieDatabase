using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Use cases for the movie collection: browse, add, edit, delete.</summary>
public sealed class MovieCatalog(IMovieRepository movies, TimeProvider clock)
{
    public Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        movies.FindAsync(id, cancellationToken);

    public async Task<MovieList> ListAsync(MovieQuery query, CancellationToken cancellationToken = default) =>
        MovieFilter.Apply(await movies.ListAsync(cancellationToken), query);

    public async Task<SaveMovieResult> AddAsync(
        MovieDetails details, IReadOnlyCollection<FormatDetails> formats, CancellationToken cancellationToken = default)
    {
        var errors = Movie.Validate(details, formats);
        if (errors.Count > 0)
        {
            return new SaveMovieResult.Invalid(errors);
        }

        var movie = Movie.Create(details, formats, clock.GetUtcNow());
        movies.Add(movie);
        await movies.SaveChangesAsync(cancellationToken);
        return new SaveMovieResult.Saved(movie);
    }

    /// <param name="expectedVersion">The <see cref="Domain.ProductBase.Version"/> the caller edited; a newer one means a conflict.</param>
    public async Task<SaveMovieResult> UpdateAsync(
        Guid id,
        int expectedVersion,
        MovieDetails details,
        IReadOnlyCollection<FormatDetails> formats,
        CancellationToken cancellationToken = default)
    {
        var movie = await movies.FindAsync(id, cancellationToken);
        if (movie is null)
        {
            return new SaveMovieResult.NotFound();
        }

        if (movie.Version != expectedVersion)
        {
            return new SaveMovieResult.Conflict();
        }

        var errors = Movie.Validate(details, formats);
        if (errors.Count > 0)
        {
            return new SaveMovieResult.Invalid(errors);
        }

        movie.Update(details, formats, clock.GetUtcNow());
        try
        {
            await movies.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new SaveMovieResult.Conflict();
        }

        return new SaveMovieResult.Saved(movie);
    }

    /// <returns><see langword="false"/> if there was no such movie.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var movie = await movies.FindAsync(id, cancellationToken);
        if (movie is null)
        {
            return false;
        }

        movies.Remove(movie);
        try
        {
            await movies.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Deleted (or changed) elsewhere in the meantime; it is gone or will be shown again on reload.
            return false;
        }

        return true;
    }
}
