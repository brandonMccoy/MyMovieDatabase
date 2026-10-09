using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Infrastructure.Metadata;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>The online databases as the PC registers them: base addresses, caching and resilience in place.</summary>
public sealed class OnlineServicesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ServiceProvider> ServicesAsync(InMemorySqlite sqlite, FakeHttp http)
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch))
            .AddShelfInfrastructure(new ShelfSettings
            {
                ConnectionString = sqlite.ConnectionString,
                CoverDirectory = Path.GetTempPath(),
                Tmdb = new TmdbSettings { ApiKey = "eyJ.test" },
            });
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => http));
        var provider = services.BuildServiceProvider();
        await provider.MigrateShelfDatabaseAsync(Ct);
        return provider;
    }

    [Fact]
    public async Task Tmdb_is_asked_once_for_the_same_film()
    {
        using var sqlite = new InMemorySqlite();
        var http = FakeHttp.Replaying("tmdb/movie-949-heat.json");
        await using var services = await ServicesAsync(sqlite, http);

        var first = await services.GetRequiredService<IMovieMetadata>().GetAsync(949, Ct);
        var second = await services.GetRequiredService<IMovieMetadata>().GetAsync(949, Ct);

        first!.Title.ShouldBe("Heat");
        second!.Title.ShouldBe("Heat");
        http.Requests.ShouldHaveSingleItem().Uri.GetLeftPart(UriPartial.Path).ShouldBe("https://api.themoviedb.org/3/movie/949");
    }

    [Fact]
    public async Task Upcitemdb_is_asked_once_for_the_same_barcode()
    {
        using var sqlite = new InMemorySqlite();
        var http = FakeHttp.Replaying("upcitemdb/fight-club-bluray.json");
        await using var services = await ServicesAsync(sqlite, http);

        await services.GetRequiredService<IBarcodeLookup>().FindProductNamesAsync(Upc.Parse("024543617907"), Ct);
        var names = await services.GetRequiredService<IBarcodeLookup>().FindProductNamesAsync(Upc.Parse("024543617907"), Ct);

        names.ShouldBe(["Mill Creek - Fight Club [BLU-RAY]"]);
        http.Requests.ShouldHaveSingleItem().Uri.GetLeftPart(UriPartial.Path).ShouldBe("https://api.upcitemdb.com/prod/trial/lookup");
    }

    [Fact]
    public async Task A_busy_upcitemdb_is_not_retried()
    {
        using var sqlite = new InMemorySqlite();
        var http = FakeHttp.Replaying("upcitemdb/too-fast.json", HttpStatusCode.TooManyRequests);
        await using var services = await ServicesAsync(sqlite, http);

        var ex = await Should.ThrowAsync<MetadataException>(() =>
            services.GetRequiredService<IBarcodeLookup>().FindProductNamesAsync(Upc.Parse("024543617907"), Ct));

        ex.Problem.ShouldBe(MetadataProblem.Busy);
        http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Posters_are_downloaded_at_the_stored_size()
    {
        using var sqlite = new InMemorySqlite();
        var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) });
        await using var services = await ServicesAsync(sqlite, http);

        var image = await services.GetRequiredService<IPosterSource>().FetchAsync("/heat.jpg", Ct);

        image.ShouldBe([0xFF, 0xD8, 0xFF]);
        http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://image.tmdb.org/t/p/w500/heat.jpg"));
    }

    [Fact]
    public async Task A_poster_that_is_gone_is_no_image()
    {
        using var sqlite = new InMemorySqlite();
        var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var services = await ServicesAsync(sqlite, http);

        (await services.GetRequiredService<IPosterSource>().FetchAsync("/gone.jpg", Ct)).ShouldBeNull();
    }
}
