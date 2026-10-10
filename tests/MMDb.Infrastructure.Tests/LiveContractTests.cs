using System.Text.Json;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

/// <summary>
/// Hit the real services to check the fixtures still match them. Never part of the normal suite:
/// <c>dotnet test --project tests/MMDb.Infrastructure.Tests -- --explicit only</c>. TMDB needs the key in the
/// app's user secrets (<c>Tmdb:ApiKey</c>) or the <c>Tmdb__ApiKey</c> environment variable, Metron its token
/// (<c>Metron:Token</c> or <c>Metron__Token</c>).
/// </summary>
public sealed class LiveContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TmdbClient Tmdb()
    {
        var key = Environment.GetEnvironmentVariable("Tmdb__ApiKey") ?? KeyFromUserSecrets("Tmdb:ApiKey");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "No TMDB key in user secrets or Tmdb__ApiKey.");
        return new TmdbClient(new HttpClient { BaseAddress = new Uri("https://api.themoviedb.org/3/") }, new TmdbSettings { ApiKey = key });
    }

    private static MetronClient Metron()
    {
        var token = Environment.GetEnvironmentVariable("Metron__Token") ?? KeyFromUserSecrets("Metron:Token");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(token), "No Metron token in user secrets or Metron__Token.");
        return new MetronClient(new HttpClient { BaseAddress = new Uri("https://metron.cloud/api/") }, new MetronSettings { Token = token });
    }

    private static string? KeyFromUserSecrets(string name)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "shelf-mmdb-app", "secrets.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var secrets = JsonDocument.Parse(File.ReadAllText(path));
        return secrets.RootElement.TryGetProperty(name, out var key) ? key.GetString() : null;
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

    [Fact(Explicit = true)]
    public async Task Metron_finds_saga_7_and_its_series()
    {
        var metron = Metron();

        // The search also finds the trade paperback numbered 7 (Metron lists trades as series of their own).
        var ids = await metron.SearchAsync(new IssueSearch { Series = "Saga", SeriesYear = 2012, Number = "7" }, Ct);
        var found = await Task.WhenAll(ids.Select(id => metron.GetIssueAsync(id, Ct)));
        found.Select(f => f?.Details.Format).ShouldContain(ComicFormat.TradePaperback);
        var issue = found.Single(f => f?.Details.Format == ComicFormat.SingleIssue);

        issue.ShouldNotBeNull().Details.ShouldSatisfyAllConditions(
            d => d.Series.ShouldBe("Saga"),
            d => d.Publisher.ShouldBe("Image Comics"),
            d => d.Writers.ShouldContain("Brian K. Vaughan"),
            d => d.CoverUrl.ShouldNotBeNull(),
            d => d.Validate().ShouldBeEmpty());
        var series = await metron.GetSeriesAsync(issue.Details.MetronSeriesId!.Value, Ct);
        series.ShouldNotBeNull().IssueNumbers.ShouldContain("7");
    }

    [Fact(Explicit = true)]
    public async Task Metron_finds_an_issue_by_its_barcode()
    {
        var metron = Metron();
        var ids = await metron.SearchAsync(new IssueSearch { Series = "Saga", SeriesYear = 2012, Number = "1" }, Ct);
        var saga1 = (await Task.WhenAll(ids.Select(id => metron.GetIssueAsync(id, Ct)))).First(f => f?.Details.Format == ComicFormat.SingleIssue)!;
        Assert.SkipWhen(saga1.Details.Barcode is null, "Metron has no barcode for Saga #1.");

        (await metron.SearchAsync(new IssueSearch { Barcode = saga1.Details.Barcode }, Ct)).ShouldContain(saga1.Details.MetronId!.Value);
    }

    [Fact(Explicit = true)]
    public async Task Open_library_knows_the_first_saga_trade()
    {
        var client = new OpenLibraryClient(new HttpClient { BaseAddress = new Uri("https://openlibrary.org/") });

        var book = await client.FindByIsbnAsync(Isbn.Parse("9781607066019"), Ct);

        book.ShouldNotBeNull().Title.ShouldBe("Saga, Volume One");
        book.Writers.ShouldContain("Brian K. Vaughan");
    }
}
