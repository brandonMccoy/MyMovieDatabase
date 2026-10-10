using System.Net;
using System.Web;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

public sealed class MetronClientTests
{
    private const string Token = "0123456789abcdef0123456789abcdef01234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetronClient Client(FakeHttp http, string? token = Token) =>
        new(http.Client("https://metron.cloud/api/"), new MetronSettings { Token = token });

    private static FakeHttp Serving(params (string PathAndQuery, string Fixture)[] answers) => new(request =>
    {
        var asked = request.RequestUri!.PathAndQuery;
        var (_, fixture) = answers.FirstOrDefault(a => a.PathAndQuery == asked);
        return fixture is null
            ? FakeHttp.Respond(HttpStatusCode.NotFound, Fixtures.Read("metron/not-found.json"))
            : FakeHttp.Respond(HttpStatusCode.OK, Fixtures.Read(fixture));
    });

    [Fact]
    public async Task A_barcode_search_asks_for_the_full_code_with_the_token()
    {
        var http = FakeHttp.Replaying("metron/search-saga-7.json");

        var ids = await Client(http).SearchAsync(new IssueSearch { Barcode = "70985301744800711" }, Ct);

        ids.ShouldBe([4521]);
        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe("/api/issue/");
        HttpUtility.ParseQueryString(request.Uri.Query)["upc"].ShouldBe("70985301744800711");
        request.Authorization.ShouldBe($"Bearer {Token}");
    }

    [Fact]
    public async Task A_text_search_sends_series_words_year_and_number()
    {
        var http = FakeHttp.Replaying("metron/search-saga-7.json");

        await Client(http).SearchAsync(new IssueSearch { Series = "Daredevil & Elektra", SeriesYear = 1964, Number = "181" }, Ct);

        var query = HttpUtility.ParseQueryString(http.Requests.ShouldHaveSingleItem().Uri.Query);
        query["series_name"].ShouldBe("Daredevil & Elektra");
        query["series_year_began"].ShouldBe("1964");
        query["number"].ShouldBe("181");
        query["upc"].ShouldBeNull();
    }

    [Fact]
    public async Task A_upc_without_its_add_on_searches_by_prefix()
    {
        var http = FakeHttp.Replaying("metron/search-none.json");

        var ids = await Client(http).SearchAsync(new IssueSearch { UpcPrefix = "709853017448", Number = "7" }, Ct);

        ids.ShouldBeEmpty();
        var query = HttpUtility.ParseQueryString(http.Requests.ShouldHaveSingleItem().Uri.Query);
        query["upc_starts_with"].ShouldBe("709853017448");
        query["number"].ShouldBe("7");
    }

    [Fact]
    public async Task An_issue_has_every_field_ready_to_edit()
    {
        var issue = await Client(FakeHttp.Replaying("metron/issue-4521-saga-7.json")).GetIssueAsync(4521, Ct);

        var details = issue.ShouldNotBeNull().Details;
        details.ShouldSatisfyAllConditions(
            d => d.Format.ShouldBe(ComicFormat.SingleIssue),
            d => d.Series.ShouldBe("Saga"),
            d => d.SeriesYear.ShouldBe(2012),
            d => d.Number.ShouldBe("7"),
            d => d.Title.ShouldBe("Chapter Seven"),
            d => d.Publisher.ShouldBe("Image"),
            d => d.CoverDate.ShouldBe(new DateOnly(2012, 11, 1)),
            d => d.Writers.ShouldBe(["Brian K. Vaughan"]),
            d => d.Artists.ShouldBe(["Fiona Staples"]),
            d => d.Summary.ShouldStartWith("Marko and Alana"),
            d => d.Barcode.ShouldBe("70985301744800711"),
            d => d.Isbn.ShouldBeNull(),
            d => d.MetronId.ShouldBe(4521),
            d => d.MetronSeriesId.ShouldBe(1234),
            d => d.CoverUrl.ShouldBe("https://static.metron.cloud/media/issue/2018/11/29/saga-7.jpg"),
            d => d.Validate().ShouldBeEmpty());
        issue.Variants.ShouldBe([new VariantFacts("Second Printing", "70985301744800712", "https://static.metron.cloud/media/variants/2018/11/29/saga-7-2nd.jpg")]);
    }

    [Fact]
    public async Task A_recorded_issue_maps_every_field()
    {
        var issue = await Client(FakeHttp.Replaying("metron/issue-8003-saga-7.json")).GetIssueAsync(8003, Ct);

        issue.ShouldNotBeNull().Details.ShouldSatisfyAllConditions(
            d => d.DisplayName.ShouldBe("Saga #7"),
            d => d.Format.ShouldBe(ComicFormat.SingleIssue),
            d => d.Title.ShouldBe("Chapter Seven"),
            d => d.Publisher.ShouldBe("Image Comics"),
            d => d.Artists.ShouldBe(["Fiona Staples"]),
            d => d.Barcode.ShouldBe("70985301166800711"),
            d => d.MetronSeriesId.ShouldBe(916),
            d => d.Validate().ShouldBeEmpty());
        issue.Variants.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trade_metron_lists_becomes_a_trade_paperback_with_its_isbn()
    {
        var trade = await Client(FakeHttp.Replaying("metron/issue-52779-saga-tpb-7.json")).GetIssueAsync(52779, Ct);

        trade.ShouldNotBeNull().Details.ShouldSatisfyAllConditions(
            d => d.Format.ShouldBe(ComicFormat.TradePaperback),
            d => d.Title.ShouldBe("Saga, Volume Seven"),
            d => d.DisplayName.ShouldBe("Saga, Volume Seven"),
            d => d.Number.ShouldBe("7"),
            d => d.Isbn.ShouldBe("9781534300606"),
            d => d.Barcode.ShouldBeNull(),
            d => d.Validate().ShouldBeEmpty());
    }

    [Fact]
    public async Task A_recorded_search_finds_the_issue_and_the_trade()
    {
        (await Client(FakeHttp.Replaying("metron/search-saga-2012-7.json")).SearchAsync(new IssueSearch { Series = "saga", SeriesYear = 2012, Number = "7" }, Ct))
            .ShouldBe([8003, 52779]);
    }

    [Fact]
    public async Task A_series_lists_every_issue_number_across_pages()
    {
        var http = Serving(
            ("/api/series/1234/", "metron/series-1234-saga.json"),
            ("/api/issue/?series_id=1234", "metron/series-1234-issues-page1.json"),
            ("/api/issue/?page=2&series_id=1234", "metron/series-1234-issues-page2.json"));

        var series = await Client(http).GetSeriesAsync(1234, Ct);

        series.ShouldNotBeNull().ShouldSatisfyAllConditions(
            s => s.Name.ShouldBe("Saga"),
            s => s.YearBegan.ShouldBe(2012),
            s => s.Publisher.ShouldBe("Image"),
            s => s.IssueNumbers.ShouldBe(["1", "2", "3", "7"]));
        http.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task An_unknown_issue_or_series_is_null()
    {
        var http = Serving();

        (await Client(http).GetIssueAsync(1, Ct)).ShouldBeNull();
        (await Client(http).GetSeriesAsync(1, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_token_nothing_is_sent()
    {
        var http = FakeHttp.Replaying("metron/search-saga-7.json");

        var ex = await Should.ThrowAsync<MetadataException>(() => Client(http, token: null).SearchAsync(new IssueSearch { Series = "Saga" }, Ct));

        ex.Problem.ShouldBe(MetadataProblem.NotConfigured);
        ex.Service.ShouldBe("Metron");
        http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("metron/no-token.json", HttpStatusCode.Unauthorized, MetadataProblem.KeyRejected)]
    [InlineData("metron/rate-limited.json", HttpStatusCode.TooManyRequests, MetadataProblem.Busy)]
    [InlineData("metron/not-found.json", HttpStatusCode.BadGateway, MetadataProblem.Unavailable)]
    public async Task Error_answers_are_reported_as_problems(string fixture, HttpStatusCode status, MetadataProblem problem)
    {
        var ex = await Should.ThrowAsync<MetadataException>(() =>
            Client(FakeHttp.Replaying(fixture, status)).SearchAsync(new IssueSearch { Series = "Saga" }, Ct));

        ex.Problem.ShouldBe(problem);
    }

    [Fact]
    public async Task A_garbled_answer_is_reported_as_unavailable()
    {
        var http = new FakeHttp(_ => FakeHttp.Respond(HttpStatusCode.OK, "<html>Cloudflare</html>"));

        var ex = await Should.ThrowAsync<MetadataException>(() => Client(http).GetIssueAsync(4521, Ct));

        ex.Problem.ShouldBe(MetadataProblem.Unavailable);
    }
}

public sealed class OpenLibraryClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OpenLibraryClient Client(FakeHttp http) => new(http.Client("https://openlibrary.org/"));

    [Fact]
    public async Task A_book_becomes_a_trade_paperback_ready_to_edit()
    {
        var http = FakeHttp.Replaying("openlibrary/saga-volume-one.json");

        var book = await Client(http).FindByIsbnAsync(Isbn.Parse("9781607066019"), Ct);

        book.ShouldNotBeNull().ShouldSatisfyAllConditions(
            b => b.Format.ShouldBe(ComicFormat.TradePaperback),
            b => b.Title.ShouldBe("Saga, Volume One"),
            b => b.Publisher.ShouldBe("Image Comics"),
            b => b.Writers.ShouldBe(["Brian K. Vaughan"]),
            b => b.Isbn.ShouldBe("9781607066019"),
            b => b.CoverUrl.ShouldBe("https://covers.openlibrary.org/b/id/12679639-L.jpg"),
            b => b.CoverDate.ShouldBeNull(), // only a year: not a cover date
            b => b.Validate().ShouldBeEmpty());
        var uri = http.Requests.ShouldHaveSingleItem().Uri;
        uri.AbsolutePath.ShouldBe("/api/books");
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["bibkeys"].ShouldBe("ISBN:9781607066019");
        query["jscmd"].ShouldBe("data");
    }

    [Fact]
    public async Task Every_author_is_kept()
    {
        var book = await Client(FakeHttp.Replaying("openlibrary/sandman-preludes.json")).FindByIsbnAsync(Isbn.Parse("9781401225759"), Ct);

        book.ShouldNotBeNull().Writers.ShouldBe(["Neil Gaiman", "Sam Kieth"]);
        book.Title.ShouldBe("Preludes & Nocturnes");
        book.Publisher.ShouldBe("Vertigo");
        book.CoverUrl.ShouldBe("https://covers.openlibrary.org/b/id/6626179-L.jpg");
    }

    [Fact]
    public async Task An_unknown_isbn_is_null()
    {
        (await Client(FakeHttp.Replaying("openlibrary/unknown.json")).FindByIsbnAsync(Isbn.Parse("9799999999990"), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Being_turned_away_is_reported_as_busy()
    {
        var ex = await Should.ThrowAsync<MetadataException>(() =>
            Client(new FakeHttp(_ => FakeHttp.Respond(HttpStatusCode.Forbidden, "{}"))).FindByIsbnAsync(Isbn.Parse("9781607066019"), Ct));

        ex.Problem.ShouldBe(MetadataProblem.Busy);
        ex.Service.ShouldBe("Open Library");
    }
}

public sealed class ComicCoversTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeHttp Image(string mediaType = "image/jpeg") => new(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new(mediaType) } } });

    [Theory]
    [InlineData("https://static.metron.cloud/media/issue/saga-7.jpg")]
    [InlineData("https://covers.openlibrary.org/b/id/12679639-L.jpg")]
    public async Task Covers_come_from_the_comic_databases(string url)
    {
        var http = Image();

        (await new ComicCovers(new HttpClient(http)).FetchAsync(url, Ct)).ShouldBe([1, 2, 3]);
    }

    [Theory]
    [InlineData("https://example.com/cover.jpg")]
    [InlineData("http://static.metron.cloud/media/issue/saga-7.jpg")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("not a link")]
    public async Task Nothing_is_fetched_from_anywhere_else(string url)
    {
        var http = Image();

        (await new ComicCovers(new HttpClient(http)).FetchAsync(url, Ct)).ShouldBeNull();
        http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_that_isnt_an_image_is_no_cover()
    {
        (await new ComicCovers(new HttpClient(Image("text/html"))).FetchAsync("https://static.metron.cloud/x.jpg", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Being_offline_is_no_cover()
    {
        var http = FakeHttp.Failing(new HttpRequestException("No such host is known."));

        (await new ComicCovers(new HttpClient(http)).FetchAsync("https://static.metron.cloud/x.jpg", Ct)).ShouldBeNull();
    }
}
