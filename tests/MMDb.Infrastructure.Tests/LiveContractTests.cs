using System.Text.Json;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

/// <summary>
/// Hit the real services to check the fixtures still match them. Never part of the normal suite:
/// <c>dotnet test --project tests/MMDb.Infrastructure.Tests -- --explicit only</c>. TMDB needs the key in the
/// app's user secrets (<c>Tmdb:ApiKey</c>) or the <c>Tmdb__ApiKey</c> environment variable.
/// </summary>
public sealed class LiveContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TmdbClient Tmdb()
    {
        var key = Environment.GetEnvironmentVariable("Tmdb__ApiKey") ?? KeyFromUserSecrets();
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "No TMDB key in user secrets or Tmdb__ApiKey.");
        return new TmdbClient(new HttpClient { BaseAddress = new Uri("https://api.themoviedb.org/3/") }, new TmdbSettings { ApiKey = key });
    }

    private static string? KeyFromUserSecrets()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "shelf-mmdb-app", "secrets.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var secrets = JsonDocument.Parse(File.ReadAllText(path));
        return secrets.RootElement.TryGetProperty("Tmdb:ApiKey", out var key) ? key.GetString() : null;
    }

    [Fact(Explicit = true)]
    public async Task Tmdb_search_finds_heat()
    {
        (await Tmdb().SearchAsync("Heat", 1995, Ct)).ShouldContain(949);
    }

    [Fact(Explicit = true)]
    public async Task Tmdb_details_for_heat_have_every_field()
    {
        var heat = await Tmdb().GetAsync(949, Ct);

        heat.ShouldNotBeNull().ShouldSatisfyAllConditions(
            h => h.Title.ShouldBe("Heat"),
            h => h.Year.ShouldBe(1995),
            h => h.Directors.ShouldContain("Michael Mann"),
            h => h.Cast.Count.ShouldBe(TmdbClient.CastLimit),
            h => h.Cast[0].Name.ShouldBe("Al Pacino"),
            h => h.Genres.ShouldContain("Crime"),
            h => h.RuntimeMinutes.ShouldNotBeNull(),
            h => h.Rating.ShouldBe("R"),
            h => h.TrailerYouTubeKey.ShouldNotBeNullOrWhiteSpace(),
            h => h.Validate().ShouldBeEmpty());
    }

    [Fact(Explicit = true)]
    public async Task Upcitemdb_names_fight_club()
    {
        var client = new UpcItemDbClient(new HttpClient { BaseAddress = new Uri("https://api.upcitemdb.com/") });

        var names = await client.FindProductNamesAsync(Upc.Parse("024543617907"), Ct);

        RetailTitle.Parse(names.ShouldHaveSingleItem()).ShouldBe(new RetailTitle("Fight Club", null, MediaFormat.BluRay));
    }
}
