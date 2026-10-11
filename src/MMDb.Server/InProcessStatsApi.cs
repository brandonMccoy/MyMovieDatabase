using MMDb.Application.Stats;
using MMDb.Contracts;
using MMDb.Contracts.Stats;
using MMDb.Server.Movies;

namespace MMDb.Server;

/// <summary><see cref="IStatsApi"/> for the PC app: counts the collection directly, one DI scope per call.</summary>
public sealed class InProcessStatsApi(IServiceScopeFactory scopes) : IStatsApi
{
    public async Task<StatsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var stats = await scope.ServiceProvider.GetRequiredService<StatsSource>().ReadAsync(cancellationToken);
        return new()
        {
            FilmCount = stats.FilmCount,
            FilmsAddedThisMonth = stats.FilmsAddedThisMonth,
            DiscCount = stats.DiscCount,
            FilmsOnSeveralFormats = stats.FilmsOnSeveralFormats,
            DigitalCount = stats.DigitalCount,
            IssueCount = stats.IssueCount,
            IssuesAddedThisMonth = stats.IssuesAddedThisMonth,
            CollectedCount = stats.CollectedCount,
            SeriesCount = stats.SeriesCount,
            CompleteSeriesCount = stats.CompleteSeriesCount,
            WishlistMovieCount = stats.WishlistMovieCount,
            WishlistComicCount = stats.WishlistComicCount,
            Formats = [.. stats.Formats.Select(f => new FormatCountDto(f.Format.ToContract(), f.Count))],
            AddedByMonth = [.. stats.AddedByMonth.Select(m => new MonthCountDto(m.Year, m.Month, m.Count))],
            Decades = [.. stats.Decades.Select(d => new DecadeCountDto(d.Decade, d.Count))],
            FilmsWithoutYear = stats.FilmsWithoutYear,
            TopDirectors = [.. stats.TopDirectors.Select(d => new NameCountDto(d.Name, d.Count))],
            Publishers = [.. stats.Publishers.Select(p => new PublisherCountDto(p.Name, p.Count))],
        };
    }
}
