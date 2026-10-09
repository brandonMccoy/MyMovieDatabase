using Microsoft.EntityFrameworkCore;
using MMDb.Application.Movies;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

internal sealed class EfMovieRepository(ShelfDbContext db) : IMovieRepository
{
    public Task<Movie?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Movies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Movie>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Movies.AsNoTracking().ToListAsync(cancellationToken);

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
