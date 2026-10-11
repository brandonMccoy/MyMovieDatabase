using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Application.Comics;

/// <summary>Port: where comics, and what Metron says about their series, are stored. One instance is one unit of work.</summary>
public interface IComicRepository
{
    Task<Comic?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Read-only: every comic.</summary>
    Task<IReadOnlyList<Comic>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only: comics with this UPC (in either of its forms), whatever their add-on.</summary>
    Task<IReadOnlyList<Comic>> ListByUpcAsync(Upc upc, CancellationToken cancellationToken = default);

    /// <summary>Read-only: comics with this ISBN.</summary>
    Task<IReadOnlyList<Comic>> ListByIsbnAsync(Isbn isbn, CancellationToken cancellationToken = default);

    /// <summary>Read-only: comics linked to any of these Metron issues.</summary>
    Task<IReadOnlyList<Comic>> ListByMetronIdsAsync(IReadOnlyCollection<int> metronIds, CancellationToken cancellationToken = default);

    Task<ComicSeries?> FindSeriesAsync(int metronId, CancellationToken cancellationToken = default);

    /// <summary>Read-only: every series fetched from Metron.</summary>
    Task<IReadOnlyList<ComicSeries>> ListSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only: every comic as the Stats screen counts it.</summary>
    Task<IReadOnlyList<ComicTally>> ListTalliesAsync(CancellationToken cancellationToken = default);

    void Add(Comic comic);

    void Remove(Comic comic);

    void AddSeries(ComicSeries series);

    /// <exception cref="Movies.ConcurrencyConflictException">The comic was changed or deleted by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the Stats screen counts of a comic copy.</summary>
public sealed record ComicTally(
    ComicFormat Format, string? Series, int? SeriesYear, string? Number, string? Publisher, int? MetronSeriesId, DateTimeOffset CreatedAt);
