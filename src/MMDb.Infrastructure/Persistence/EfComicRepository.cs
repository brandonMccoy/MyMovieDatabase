using Microsoft.EntityFrameworkCore;
using MMDb.Application.Comics;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Infrastructure.Persistence;

internal sealed class EfComicRepository(ShelfDbContext db) : IComicRepository
{
    public Task<Comic?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Comics.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Comic>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Comics.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Comic>> ListByUpcAsync(Upc upc, CancellationToken cancellationToken = default)
    {
        var other = upc.AlternateForm ?? upc.Value;
        return await db.Comics.AsNoTracking()
            .Where(c => EF.Property<string>(c, ComicConfiguration.UpcField) == upc.Value || EF.Property<string>(c, ComicConfiguration.UpcField) == other)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Comic>> ListByIsbnAsync(Isbn isbn, CancellationToken cancellationToken = default) =>
        await db.Comics.AsNoTracking().Where(c => c.Isbn == isbn).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Comic>> ListByMetronIdsAsync(IReadOnlyCollection<int> metronIds, CancellationToken cancellationToken = default) =>
        await db.Comics.AsNoTracking().Where(c => c.MetronId != null && metronIds.Contains(c.MetronId.Value)).ToListAsync(cancellationToken);

    public Task<ComicSeries?> FindSeriesAsync(int metronId, CancellationToken cancellationToken = default) =>
        db.ComicSeries.FirstOrDefaultAsync(s => s.MetronId == metronId, cancellationToken);

    public async Task<IReadOnlyList<ComicSeries>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
        await db.ComicSeries.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ComicTally>> ListTalliesAsync(CancellationToken cancellationToken = default) =>
        await db.Comics.AsNoTracking()
            .Select(c => new ComicTally(c.Format, c.Series, c.SeriesYear, c.Number, c.Publisher, c.MetronSeriesId, c.CreatedAt))
            .ToListAsync(cancellationToken);

    public void Add(Comic comic) => db.Comics.Add(comic);

    public void Remove(Comic comic) => db.Comics.Remove(comic);

    public void AddSeries(ComicSeries series) => db.ComicSeries.Add(series);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The comic was changed or deleted since it was loaded.", ex);
        }
    }
}
