using MMDb.Application.Covers;
using MMDb.Application.Metadata;
using MMDb.Application.Vault;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Use cases for the movie collection: browse, add, edit, delete. Every change is mirrored into the vault.</summary>
public sealed class MovieCatalog(IMovieRepository movies, TimeProvider clock, IPosterSource posters, ICoverStore coverStore, VaultMirror vault, VaultContentSource content)
{
    private readonly CoverKeeper _covers = new(coverStore);

    public Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        movies.FindAsync(id, cancellationToken);

    public async Task<MovieList> ListAsync(MovieQuery query, CancellationToken cancellationToken = default)
    {
        var crediting = new Dictionary<string, IReadOnlySet<Guid>>();
        foreach (var term in MovieFilter.Terms(query).Distinct())
        {
            crediting[term] = await movies.FindCreditingAsync(term, cancellationToken);
        }

        return MovieFilter.Apply(await movies.ListAsync(cancellationToken), query, crediting);
    }

    /// <summary>
    /// Adds a film, downloading its poster if it has one. A film already owned (same TMDB id) is not added
    /// twice: the new formats are added to it instead and its details are left as they are.
    /// </summary>
    public async Task<SaveMovieResult> AddAsync(
        MovieDetails details, IReadOnlyCollection<FormatDetails> formats, CancellationToken cancellationToken = default)
    {
        var errors = Movie.Validate(details, formats);
        if (errors.Count > 0)
        {
            return new SaveMovieResult.Invalid(errors);
        }

        if (details.TmdbId is { } tmdbId && await movies.FindByTmdbIdAsync(tmdbId, cancellationToken) is { } owned)
        {
            if (owned.AddFormats(formats, clock.GetUtcNow()).Count == 0)
            {
                return new SaveMovieResult.AlreadyOwned(owned);
            }

            return await SaveAsync(owned, cancellationToken);
        }

        var movie = Movie.Create(details, formats, clock.GetUtcNow());
        movies.Add(movie);
        return await SaveAsync(movie, cancellationToken);
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
        return await SaveAsync(movie, cancellationToken);
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

        _covers.Forget(movie);
        await vault.RemovedAsync(movie.Id, content);
        return true;
    }

    private async Task<SaveMovieResult> SaveAsync(Movie movie, CancellationToken cancellationToken)
    {
        try
        {
            var poster = movie.PosterPath;
            await _covers.SaveAsync(
                movie,
                poster is null ? null : CoverKeeper.CoverName(movie.Id, poster.TrimStart('/')),
                ct => posters.FetchAsync(poster!, ct),
                () => movies.SaveChangesAsync(cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new SaveMovieResult.Conflict();
        }

        await vault.MovieSavedAsync(movie, content);
        return new SaveMovieResult.Saved(movie);
    }
}
