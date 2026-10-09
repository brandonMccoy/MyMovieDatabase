using Microsoft.EntityFrameworkCore;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

internal sealed class EfMovieRepository(ShelfDbContext db) : IMovieRepository
{
    public Task<Movie?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Movies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Movie>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Movies.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Movie?> FindByUpcAsync(Upc upc, CancellationToken cancellationToken = default)
    {
        var other = upc.AlternateForm is { } alternate ? Upc.Parse(alternate) : upc;
        return db.Movies.FirstOrDefaultAsync(m => m.Formats.Any(f => f.Upc == upc || f.Upc == other), cancellationToken);
    }

    public Task<Movie?> FindByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default) =>
        db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken);

    public async Task<IReadOnlyList<Movie>> ListByTmdbIdsAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken = default) =>
        await db.Movies.AsNoTracking().Where(m => m.TmdbId != null && tmdbIds.Contains(m.TmdbId.Value)).ToListAsync(cancellationToken);

    public void Add(Movie movie) => db.Movies.Add(movie);

    public void Remove(Movie movie) => db.Movies.Remove(movie);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The movie was changed or deleted since it was loaded.", ex);
        }
    }
}
