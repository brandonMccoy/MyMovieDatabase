using MMDb.Contracts.Stats;

namespace MMDb.Contracts;

/// <summary>The Stats screen's numbers. PC only for now: the phone gets them with its offline copy.</summary>
public interface IStatsApi
{
    Task<StatsDto> GetAsync(CancellationToken cancellationToken = default);
}
