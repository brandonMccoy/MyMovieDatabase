using MMDb.Application.Covers;
using MMDb.Application.Metadata;
using MMDb.Application.Vault;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Movies;

/// <summary>
/// Use cases for the movie collection: browse, add, edit, delete. Every change is mirrored into the vault.
/// A film saved on the format it was wanted on leaves the wishlist.
/// </summary>
public sealed class MovieCatalog(
    IMovieRepository movies, IWishlistRepository wishlist, TimeProvider clock, IPosterSource posters, ICoverStore coverStore, VaultMirror vault, VaultContentSource content)
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

        // The same names "Top actors" counts, so a bar and the list it opens agree.
        var starring = query.Actor is { } actor
            ? (await movies.ListTopBilledAsync(MovieFilter.TopBilled, cancellationToken)).Where(b => MovieFilter.SameName(b.Name, actor)).Select(b => b.MovieId).ToHashSet()
            : null;

        return MovieFilter.Apply(await movies.ListAsync(cancellationToken), query, crediting, starring);
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

    /// <summary>
    /// Points a film's digital copy at a video file on the PC; a film not owned digitally yet now is. Its
    /// details are left as they are.
    /// </summary>
    /// <param name="replace">Whether a file the film already has may be swapped for this one.</param>
    /// <returns><see cref="SaveMovieResult.AlreadyOwned"/> if the film has another file and it may not be replaced.</returns>
    public async Task<SaveMovieResult> LinkFileAsync(Guid id, string path, bool replace, CancellationToken cancellationToken = default)
    {
        var movie = await movies.FindAsync(id, cancellationToken);
        if (movie is null)
        {
            return new SaveMovieResult.NotFound();
        }

        var current = movie.Formats.FirstOrDefault(f => f.Format == MediaFormat.Digital)?.FilePath;
        if (current is not null && !replace && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            return new SaveMovieResult.AlreadyOwned(movie);
        }

        movie.LinkFile(path, clock.GetUtcNow());
        return await SaveAsync(movie, cancellationToken);
    }

    /// <summary>Stops owning a film on one format; its other formats and details stay.</summary>
    /// <returns><see cref="SaveMovieResult.Invalid"/> if it is the only format the film is owned on, or it isn't owned on it.</returns>
    public async Task<SaveMovieResult> RemoveFormatAsync(Guid id, MediaFormat format, CancellationToken cancellationToken = default)
    {
        var movie = await movies.FindAsync(id, cancellationToken);
        if (movie is null)
        {
            return new SaveMovieResult.NotFound();
        }

        return movie.RemoveFormat(format, clock.GetUtcNow())
            ? await SaveAsync(movie, cancellationToken)
            : new SaveMovieResult.Invalid(FormatDetails.ValidateSet([]));
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
        // Off the wishlist in the same write that saves the film: the repositories share one unit of work.
        var granted = (await wishlist.FindMoviesForAsync(movie.TmdbId is { } tmdbId ? [tmdbId] : [], cancellationToken))
            .Where(wish => wish.IsFor(movie.Details) && movie.Owns(wish.Format))
            .ToList();
        granted.ForEach(wishlist.Remove);

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

        granted.ForEach(_covers.Forget);
        await vault.MovieSavedAsync(movie, content);
        if (granted.Count > 0)
        {
            await vault.WishlistChangedAsync(content);
        }

        return new SaveMovieResult.Saved(movie);
    }
}
