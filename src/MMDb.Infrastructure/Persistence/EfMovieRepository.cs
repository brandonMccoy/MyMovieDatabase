using Microsoft.EntityFrameworkCore;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

internal sealed class EfMovieRepository(ShelfDbContext db) : IMovieRepository
{
    public Task<Movie?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Movies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<IReadOnlyList<MovieListing>> ListAsync(CancellationToken cancellationToken = default) =>
        ListingsAsync(db.Movies, cancellationToken);

    public async Task<IReadOnlyList<Movie>> ListInFullAsync(CancellationToken cancellationToken = default) =>
        await db.Movies.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> FindCreditingAsync(string name, CancellationToken cancellationToken = default)
    {
        var folded = PeopleText.Fold(name);
        var ids = await db.Movies
            .Where(m => EF.Property<string?>(m, PeopleText.Column)!.Contains(folded))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public Task<Movie?> FindByUpcAsync(Upc upc, CancellationToken cancellationToken = default)
    {
        var other = upc.AlternateForm is { } alternate ? Upc.Parse(alternate) : upc;
        return db.Movies.FirstOrDefaultAsync(m => m.Formats.Any(f => f.Upc == upc || f.Upc == other), cancellationToken);
    }

    public Task<Movie?> FindByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default) =>
        db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId, cancellationToken);

    public Task<IReadOnlyList<MovieListing>> ListByTmdbIdsAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken = default) =>
        ListingsAsync(db.Movies.Where(m => m.TmdbId != null && tmdbIds.Contains(m.TmdbId.Value)), cancellationToken);

    // Only the columns a list shows or filters on: the credits of a few thousand films are megabytes of JSON.
    private static async Task<IReadOnlyList<MovieListing>> ListingsAsync(IQueryable<Movie> movies, CancellationToken cancellationToken)
    {
        var rows = await movies.AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.Year,
                m.Directors,
                m.Genres,
                m.CreatedAt,
                m.TmdbId,
                m.CoverImage,
                Formats = m.Formats.Select(f => new { f.Format, f.AddedAt }).ToList(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(m => new MovieListing(
            m.Id,
            m.Title,
            m.Year,
            m.Directors,
            m.Genres,
            [.. m.Formats.Select(f => f.Format).Order()],
            m.CreatedAt,
            m.Formats.Count == 0 ? m.CreatedAt : m.Formats.Max(f => f.AddedAt),
            m.TmdbId,
            m.CoverImage))];
    }

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
