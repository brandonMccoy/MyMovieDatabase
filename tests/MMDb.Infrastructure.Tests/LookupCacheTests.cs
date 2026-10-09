using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure.Tests;

public sealed class LookupCacheTests
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(TestDatabase database, FakeHttp http) =>
        new(new LookupCacheHandler("tmdb", _ => MaxAge, new LookupCache(database.Options), _clock) { InnerHandler = http })
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
        };

    private static FakeHttp Counting(HttpStatusCode status = HttpStatusCode.OK)
    {
        var calls = 0;
        return new FakeHttp(_ => FakeHttp.Respond(status, $$"""{"call":{{++calls}}}"""));
    }

    [Fact]
    public async Task A_repeated_request_is_answered_from_the_cache()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting();
        using var client = Client(database, http);

        var first = await client.GetStringAsync("movie/949", Ct);
        var second = await client.GetStringAsync("movie/949", Ct);

        second.ShouldBe(first);
        http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_cache_survives_restarts()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting();
        using (var before = Client(database, http))
        {
            await before.GetStringAsync("movie/949", Ct);
        }

        using var after = Client(database, http);
        await after.GetStringAsync("movie/949", Ct);

        http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Different_requests_are_kept_apart()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting();
        using var client = Client(database, http);

        var heat = await client.GetStringAsync("movie/949", Ct);
        var alien = await client.GetStringAsync("movie/348", Ct);

        alien.ShouldNotBe(heat);
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_old_answer_is_fetched_again()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting();
        using var client = Client(database, http);
        await client.GetStringAsync("movie/949", Ct);

        _clock.Advance(MaxAge + TimeSpan.FromMinutes(1));
        var refreshed = await client.GetStringAsync("movie/949", Ct);
        var cached = await client.GetStringAsync("movie/949", Ct);

        refreshed.ShouldBe("""{"call":2}""");
        cached.ShouldBe(refreshed);
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Failures_are_not_kept()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting(HttpStatusCode.ServiceUnavailable);
        using var client = Client(database, http);

        (await client.GetAsync("movie/949", Ct)).Dispose();
        (await client.GetAsync("movie/949", Ct)).Dispose();

        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Api_keys_never_reach_the_database()
    {
        using var database = await TestDatabase.MigratedAsync();
        using var client = Client(database, Counting());

        await client.GetStringAsync("search/movie?query=heat&api_key=secret-key-123", Ct);

        await using var db = database.NewContext();
        var entry = (await db.LookupCache.ToListAsync(Ct)).ShouldHaveSingleItem();
        entry.Key.ShouldBe("/3/search/movie?query=heat");
        entry.Provider.ShouldBe("tmdb");
    }

    [Fact]
    public async Task The_same_question_with_another_key_is_still_answered_from_the_cache()
    {
        using var database = await TestDatabase.MigratedAsync();
        var http = Counting();
        using var client = Client(database, http);

        await client.GetStringAsync("search/movie?query=heat&api_key=old", Ct);
        await client.GetStringAsync("search/movie?query=heat&api_key=new", Ct);

        http.Requests.Count.ShouldBe(1);
    }
}
