using System.Text.Json;
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

    public async Task<IReadOnlyList<DigitalCopy>> ListDigitalAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Movies.AsNoTracking()
            .Where(m => m.Formats.Any(f => f.Format == MediaFormat.Digital))
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.Year,
                m.TmdbId,
                m.CoverImage,
                Formats = m.Formats.Select(f => new { f.Format, f.FilePath }).ToList(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(m => new DigitalCopy(
            m.Id,
            m.Title,
            m.Year,
            m.Formats.First(f => f.Format == MediaFormat.Digital).FilePath,
            [.. m.Formats.Select(f => f.Format).Order()],
            m.TmdbId,
            m.CoverImage))];
    }

    // The four columns that are counted, and each format with when it was added.
    public async Task<IReadOnlyList<FilmTally>> ListTalliesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Movies.AsNoTracking()
            .Select(m => new
            {
                m.Year,
                m.Directors,
                m.CreatedAt,
                Formats = m.Formats.Select(f => new { f.Format, f.AddedAt }).ToList(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(m => new FilmTally(m.Year, m.Directors, m.CreatedAt, [.. m.Formats.Select(f => new FormatTally(f.Format, f.AddedAt))]))];
    }

    // Picked out of the cast's JSON by SQLite, so the long casts never leave the database. One json_extract with
    // a path per name answers with a JSON array of them, in half the time json_each takes to walk every cast.
    public async Task<IReadOnlyList<BilledName>> ListTopBilledAsync(int top, CancellationToken cancellationToken = default)
    {
        // Two paths at least: asked for one, json_extract answers with the bare name instead of an array.
        var paths = string.Join(", ", Enumerable.Range(0, Math.Max(top, 2)).Select(i => FormattableString.Invariant($"'$[{i}].name'")));
        var sql = $"""SELECT "Id" AS "MovieId", json_extract("Cast", {paths}) AS "Names" FROM "Movies" """;
        var rows = await db.Database.SqlQueryRaw<TopBilledRow>(sql).ToListAsync(cancellationToken);
        return [.. rows.SelectMany(row => (JsonSerializer.Deserialize<string?[]>(row.Names) ?? [])
            .Take(top)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => new BilledName(row.MovieId, name!)))];
    }

    private sealed record TopBilledRow(Guid MovieId, string Names);

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
