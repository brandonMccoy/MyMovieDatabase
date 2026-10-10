using System.Net;
using System.Web;
using MMDb.Application.Metadata;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

public sealed class TmdbClientTests
{
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.test-token";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TmdbClient Client(FakeHttp http, string? key = Token) =>
        new(http.Client("https://api.themoviedb.org/3/"), new TmdbSettings { ApiKey = key });

    private static async Task<MovieDetails> HeatAsync()
    {
        var details = await Client(FakeHttp.Replaying("tmdb/movie-949-heat.json")).GetAsync(949, Ct);
        return details.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_search_returns_tmdb_ids_best_first()
    {
        var ids = await Client(FakeHttp.Replaying("tmdb/search-heat.json")).SearchAsync("heat", null, Ct);

        ids.ShouldBe([949, 31939, 42113]);
    }

    [Fact]
    public async Task A_search_lists_each_film_with_its_year_poster_and_original_title()
    {
        var hits = await Client(FakeHttp.Replaying("tmdb/search-parasite-2019.json")).FindAsync("parasite", 2019, Ct);

        hits.ShouldBe(
        [
            new(496243, "Parasite", 2019, "/7IiTTgloJzvGI1TAYymCfbfl3vT.jpg", "기생충"),
            new(1100466, "Sealed Video 44: Parasite", 2019, "/7lUu9vV2tgH2ZGbBaIo5vSLvFle.jpg", "封印映像44 寄生虫"),
            new(1309516, "More Animated Worker and Parasite", null),
        ]);
    }

    [Fact]
    public async Task Listing_films_asks_the_same_search_as_their_ids()
    {
        var http = FakeHttp.Replaying("tmdb/search-heat.json");

        var hits = await Client(http).FindAsync("heat", 1995, Ct);

        hits.Select(h => (h.TmdbId, h.Title, h.Year, h.OriginalTitle)).ShouldBe([(949, "Heat", 1995, null), (31939, "Heat", 1986, null), (42113, "Heat and Dust", 1983, null)]);
        var uri = http.Requests.ShouldHaveSingleItem().Uri;
        uri.AbsolutePath.ShouldBe("/3/search/movie");
        HttpUtility.ParseQueryString(uri.Query)["year"].ShouldBe("1995");
    }

    [Fact]
    public async Task A_search_sends_the_title_and_year()
    {
        var http = FakeHttp.Replaying("tmdb/search-heat.json");

        await Client(http).SearchAsync("heat & dust", 1983, Ct);

        var uri = http.Requests.ShouldHaveSingleItem().Uri;
        uri.AbsolutePath.ShouldBe("/3/search/movie");
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["query"].ShouldBe("heat & dust");
        query["year"].ShouldBe("1983");
        query["include_adult"].ShouldBe("false");
        query["language"].ShouldBe("en-US");
    }

    [Fact]
    public async Task A_read_access_token_goes_in_the_authorization_header()
    {
        var http = FakeHttp.Replaying("tmdb/search-heat.json");

        await Client(http).SearchAsync("heat", null, Ct);

        var request = http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe($"Bearer {Token}");
        request.Uri.Query.ShouldNotContain("api_key");
    }

    [Fact]
    public async Task An_older_api_key_goes_in_the_query()
    {
        var http = FakeHttp.Replaying("tmdb/search-heat.json");

        await Client(http, key: "0123456789abcdef").SearchAsync("heat", null, Ct);

        var request = http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBeNull();
        HttpUtility.ParseQueryString(request.Uri.Query)["api_key"].ShouldBe("0123456789abcdef");
    }

    [Fact]
    public async Task Details_ask_for_credits_videos_and_release_dates_in_one_request()
    {
        var http = FakeHttp.Replaying("tmdb/movie-949-heat.json");

        await Client(http).GetAsync(949, Ct);

        var uri = http.Requests.ShouldHaveSingleItem().Uri;
        uri.AbsolutePath.ShouldBe("/3/movie/949");
        HttpUtility.ParseQueryString(uri.Query)["append_to_response"].ShouldBe("credits,videos,release_dates");
    }

    [Fact]
    public async Task Details_become_editable_movie_details()
    {
        var heat = await HeatAsync();

        heat.TmdbId.ShouldBe(949);
        heat.Title.ShouldBe("Heat");
        heat.Year.ShouldBe(1995);
        heat.RuntimeMinutes.ShouldBe(170);
        heat.Genres.ShouldBe(["Crime", "Drama", "Action"]);
        heat.Overview.ShouldStartWith("Obsessive master thief Neil McCauley");
        heat.PosterPath.ShouldBe("/umSVjVdbVwtx5ryCA2QXL44Durm.jpg");
        heat.Validate().ShouldBeEmpty();
    }

    [Fact]
    public async Task Directors_come_from_the_crew_once_each()
    {
        (await HeatAsync()).Directors.ShouldBe(["Michael Mann"]);
    }

    [Fact]
    public async Task The_whole_cast_is_kept_in_billing_order()
    {
        var cast = (await HeatAsync()).Cast;

        cast.Count.ShouldBe(12);
        cast[0].ShouldBe(new CastMember("Al Pacino", "Lt. Vincent Hanna"));
        cast[1].ShouldBe(new CastMember("Robert De Niro", "Neil McCauley"));
        cast.Select(c => c.Name).TakeLast(2).ShouldBe(["Ted Levine", "Dennis Haysbert"]);
    }

    [Fact]
    public async Task The_crew_comes_with_jobs_and_departments_in_credit_order()
    {
        (await HeatAsync()).Normalized().Crew.ShouldBe(
        [
            new("Michael Waxman", "First Assistant Director", "Directing"),
            new("Michael Mann", "Screenplay", "Writing"),
            new("Art Linson", "Producer", "Production"),
            new("Michael Mann", "Producer", "Production"),
            new("Arnon Milchan", "Executive Producer", "Production"),
            new("Dante Spinotti", "Director of Photography", "Camera"),
            new("Pasquale Buba", "Editor", "Editing"),
            new("Elliot Goldenthal", "Original Music Composer", "Sound"),
            new("Neil Spisak", "Production Design", "Art"),
        ]);
    }

    [Fact]
    public async Task Directors_are_not_listed_again_among_the_crew()
    {
        var heat = await HeatAsync();

        heat.Crew.ShouldNotContain(c => c.Job == "Director");
        heat.Validate().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_rating_is_the_us_theatrical_certification()
    {
        (await HeatAsync()).Rating.ShouldBe("R");
    }

    [Fact]
    public async Task The_trailer_is_the_newest_official_youtube_trailer()
    {
        (await HeatAsync()).TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ");
    }

    [Fact]
    public async Task Missing_details_are_left_empty()
    {
        var film = await Client(FakeHttp.Replaying("tmdb/movie-sparse.json")).GetAsync(1, Ct);

        film.ShouldNotBeNull().Normalized().ShouldSatisfyAllConditions(
            f => f.Title.ShouldBe("Untitled Short"),
            f => f.Year.ShouldBeNull(),
            f => f.RuntimeMinutes.ShouldBeNull(),
            f => f.Rating.ShouldBeNull(),
            f => f.Overview.ShouldBeNull(),
            f => f.PosterPath.ShouldBeNull(),
            f => f.TrailerYouTubeKey.ShouldBeNull(),
            f => f.Directors.ShouldBeEmpty(),
            f => f.Cast.ShouldBeEmpty(),
            f => f.Crew.ShouldBeEmpty());
    }

    [Fact]
    public async Task An_unknown_film_is_null()
    {
        (await Client(FakeHttp.Replaying("tmdb/not-found.json", HttpStatusCode.NotFound)).GetAsync(404404, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var http = FakeHttp.Replaying("tmdb/search-heat.json");

        var ex = await Should.ThrowAsync<MetadataException>(() => Client(http, key: " ").SearchAsync("heat", null, Ct));

        ex.Problem.ShouldBe(MetadataProblem.NotConfigured);
        ex.Service.ShouldBe("TMDB");
        http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("tmdb/invalid-key.json", HttpStatusCode.Unauthorized, MetadataProblem.KeyRejected)]
    [InlineData("tmdb/rate-limited.json", HttpStatusCode.TooManyRequests, MetadataProblem.Busy)]
    [InlineData("tmdb/not-found.json", HttpStatusCode.ServiceUnavailable, MetadataProblem.Unavailable)]
    public async Task Error_answers_are_reported_as_problems(string fixture, HttpStatusCode status, MetadataProblem problem)
    {
        var ex = await Should.ThrowAsync<MetadataException>(() => Client(FakeHttp.Replaying(fixture, status)).SearchAsync("heat", null, Ct));

        ex.Problem.ShouldBe(problem);
    }

    [Fact]
    public async Task Being_offline_is_reported_as_unavailable()
    {
        var ex = await Should.ThrowAsync<MetadataException>(() =>
            Client(FakeHttp.Failing(new HttpRequestException("No such host is known."))).SearchAsync("heat", null, Ct));

        ex.Problem.ShouldBe(MetadataProblem.Unavailable);
    }

    [Fact]
    public async Task A_garbled_answer_is_reported_as_unavailable()
    {
        var http = new FakeHttp(_ => FakeHttp.Respond(HttpStatusCode.OK, "<html>Service down</html>"));

        var ex = await Should.ThrowAsync<MetadataException>(() => Client(http).GetAsync(949, Ct));

        ex.Problem.ShouldBe(MetadataProblem.Unavailable);
    }

    [Fact]
    public async Task Cancelling_is_not_a_problem_with_tmdb()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Client(FakeHttp.Replaying("tmdb/search-heat.json")).SearchAsync("heat", null, cancelled.Token));
    }
}
