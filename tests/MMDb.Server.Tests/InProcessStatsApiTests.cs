using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Stats;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The PC wiring for the Stats screen: what was saved through the API is what gets counted.</summary>
public sealed class InProcessStatsApiTests : IAsyncLifetime
{
    private readonly InMemorySqlite _sqlite = new();
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero)));
        _services = new FakeOnline().ReplaceIn(services).BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task The_screen_is_handed_every_number_in_the_contracts_terms()
    {
        await Api.AddMovieAsync(new() { Title = "Heat", Year = 1995, Directors = ["Michael Mann"], Formats = [new(MovieFormatKind.Uhd4K), new(MovieFormatKind.Digital)] }, Ct);
        await Api.AddComicAsync(new() { Series = "Daredevil", SeriesYear = 1964, Number = "181", Publisher = "Marvel" }, Ct);
        await Api.AddComicAsync(new() { Series = "Mystery", Number = "1" }, Ct);
        await Api.WantMovieAsync(new() { Title = "Dune", Year = 2021, Format = MovieFormatKind.Uhd4K }, Ct);

        var stats = await _services.GetRequiredService<IStatsApi>().GetAsync(Ct);

        stats.FilmCount.ShouldBe(1);
        stats.FilmsAddedThisMonth.ShouldBe(1);
        stats.DiscCount.ShouldBe(1);
        stats.FilmsOnSeveralFormats.ShouldBe(1);
        stats.DigitalCount.ShouldBe(1);
        stats.IssueCount.ShouldBe(2);
        stats.IssuesAddedThisMonth.ShouldBe(2);
        stats.CollectedCount.ShouldBe(0);
        stats.SeriesCount.ShouldBe(2);
        stats.CompleteSeriesCount.ShouldBe(0);
        stats.WishlistMovieCount.ShouldBe(1);
        stats.WishlistComicCount.ShouldBe(0);
        stats.Formats.ShouldBe(
            [new(MovieFormatKind.Uhd4K, 1), new(MovieFormatKind.BluRay, 0), new(MovieFormatKind.Dvd, 0), new(MovieFormatKind.Digital, 1)]);
        stats.AddedByMonth.Count.ShouldBe(12);
        stats.AddedByMonth[^1].ShouldBe(new MonthCountDto(2026, 1, 4));
        stats.Decades.ShouldBe([new DecadeCountDto(1990, 1)]);
        stats.FilmsWithoutYear.ShouldBe(0);
        stats.TopDirectors.ShouldBe([new NameCountDto("Michael Mann", 1)]);
        stats.Publishers.ShouldBe([new PublisherCountDto("Marvel", 1), new PublisherCountDto(null, 1)]);
    }
}
