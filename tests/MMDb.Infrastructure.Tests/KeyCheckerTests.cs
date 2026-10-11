using System.Net;
using MMDb.Application.Metadata;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

/// <summary>Asking TMDB and Metron whether they take a key, as Settings does before it keeps one.</summary>
public sealed class KeyCheckerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KeyChecker Checker(FakeHttp http) => new(new HttpClient(http, disposeHandler: false));

    private static FakeHttp Answering(HttpStatusCode status, string body = "{}") => new(_ => FakeHttp.Respond(status, body));

    [Fact]
    public async Task A_tmdb_read_access_token_is_sent_as_a_bearer_token()
    {
        var http = Answering(HttpStatusCode.OK, """{"success":true,"status_code":1,"status_message":"Success."}""");

        var check = await Checker(http).CheckTmdbAsync(" eyJ.token ", Ct);

        check.ShouldBe(KeyCheck.Accepted);
        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri("https://api.themoviedb.org/3/authentication"));
        request.Authorization.ShouldBe("Bearer eyJ.token");
    }

    [Fact]
    public async Task An_older_tmdb_key_is_sent_in_the_query()
    {
        var http = Answering(HttpStatusCode.OK);

        var check = await Checker(http).CheckTmdbAsync("0123abcd", Ct);

        check.ShouldBe(KeyCheck.Accepted);
        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri("https://api.themoviedb.org/3/authentication?api_key=0123abcd"));
        request.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task A_key_tmdb_turns_down_is_rejected()
    {
        var http = FakeHttp.Replaying("tmdb/invalid-key.json", HttpStatusCode.Unauthorized);

        (await Checker(http).CheckTmdbAsync("wrong", Ct)).ShouldBe(KeyCheck.Rejected);
    }

    [Fact]
    public async Task A_metron_token_is_tried_on_the_smallest_search()
    {
        var http = FakeHttp.Replaying("metron/search-none.json");

        var check = await Checker(http).CheckMetronAsync(" token-1 ", Ct);

        check.ShouldBe(KeyCheck.Accepted);
        var request = http.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri("https://metron.cloud/api/issue/?upc=000000000000"));
        request.Authorization.ShouldBe("Bearer token-1");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_token_metron_turns_down_is_rejected(HttpStatusCode status)
    {
        var http = FakeHttp.Replaying("metron/no-token.json", status);

        (await Checker(http).CheckMetronAsync("wrong", Ct)).ShouldBe(KeyCheck.Rejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_service_in_trouble_says_nothing_about_the_key(HttpStatusCode status)
    {
        var checker = Checker(Answering(status));

        (await checker.CheckTmdbAsync("key", Ct)).ShouldBe(KeyCheck.Unknown);
        (await checker.CheckMetronAsync("token", Ct)).ShouldBe(KeyCheck.Unknown);
    }

    [Fact]
    public async Task Being_offline_says_nothing_about_the_key()
    {
        var checker = Checker(FakeHttp.Failing(new HttpRequestException("No such host is known.")));

        (await checker.CheckTmdbAsync("key", Ct)).ShouldBe(KeyCheck.Unknown);
        (await checker.CheckMetronAsync("token", Ct)).ShouldBe(KeyCheck.Unknown);
    }
}
