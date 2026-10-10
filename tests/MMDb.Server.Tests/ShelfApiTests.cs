using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Phone;
using MMDb.Contracts.Scans;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The web API a paired phone talks to: who gets in, status codes, and the JSON on the wire.</summary>
public sealed class ShelfApiTests : IAsyncLifetime
{
    private const string HeatUpc = "786936894424";

    private TestPc _pc = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Genres = ["Crime"],
        TmdbId = 949,
        Formats = [new(MovieFormatKind.Uhd4K, Upc: HeatUpc)],
    };

    private static SaveComicRequest Saga7 => new() { Series = "Saga", SeriesYear = 2012, Number = "7", Publisher = "Image" };

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _http = _pc.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _pc.DisposeAsync();
    }

    private async Task PairAsync() => _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _pc.PairAsync());

    private Task<HttpResponseMessage> GetAsync(string path) => _http.GetAsync(new Uri(path, UriKind.Relative), Ct);

    private Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string path, T body) =>
        _http.SendAsync(new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body, options: ShelfJson.Options) }, Ct);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        where T : class =>
        (await response.Content.ReadFromJsonAsync<T>(ShelfJson.Options, Ct)).ShouldNotBeNull();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    [Fact]
    public async Task Health_answers_anyone_with_camel_case_json()
    {
        using var response = await GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("""{"status":"ok"}""");
    }

    [Fact]
    public async Task Unversioned_api_path_is_not_found()
    {
        using var response = await GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "/api/v1/session")]
    [InlineData("GET", "/api/v1/movies")]
    [InlineData("GET", "/api/v1/movies/lookup?q=heat")]
    [InlineData("GET", "/api/v1/movies/0198c1a0-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/v1/movies/0198c1a0-0000-7000-8000-000000000001")]
    [InlineData("GET", "/api/v1/comics")]
    [InlineData("GET", "/api/v1/comics/lookup?q=saga")]
    [InlineData("DELETE", "/api/v1/comics/0198c1a0-0000-7000-8000-000000000001")]
    [InlineData("GET", "/api/v1/covers/heat.jpg")]
    public async Task A_device_that_isnt_paired_gets_nothing(string method, string path)
    {
        using var response = await _http.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await JsonAsync(response)).GetProperty("title").GetString().ShouldBe("This phone isn't paired with the PC.");
    }

    [Fact]
    public async Task Saving_and_scanning_need_a_paired_device_too()
    {
        using var movie = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat);
        using var comic = await SendJsonAsync(HttpMethod.Post, "/api/v1/comics", Saga7);
        using var scan = await SendJsonAsync(HttpMethod.Post, "/api/v1/scans", new ScanRequest(HeatUpc));

        new[] { movie.StatusCode, comic.StatusCode, scan.StatusCode }.ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        (await _pc.Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_made_up_token_is_turned_down()
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "made-up");

        using var response = await GetAsync("/api/v1/movies");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_code_on_the_pc_pairs_a_phone_and_gives_it_a_token()
    {
        using var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/pair", new PairRequest(_pc.PairingCode, "Pixel 8"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paired = await ReadAsync<PairResponse>(response);
        paired.PcName.ShouldBe(TestPc.Name);
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            d => d.Id.ShouldBe(paired.DeviceId),
            d => d.Name.ShouldBe("Pixel 8"));

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paired.Token);
        using var session = await GetAsync("/api/v1/session");
        (await ReadAsync<SessionResponse>(session)).ShouldBe(new SessionResponse(TestPc.Name, "Pixel 8"));
    }

    [Fact]
    public async Task A_wrong_code_is_forbidden_and_a_code_works_only_once()
    {
        var code = _pc.PairingCode;
        var wrong = code == "111111" ? "222222" : "111111";

        using var mistyped = await SendJsonAsync(HttpMethod.Post, "/api/v1/pair", new PairRequest(wrong, "Pixel 8"));
        using var first = await SendJsonAsync(HttpMethod.Post, "/api/v1/pair", new PairRequest(code, "Pixel 8"));
        using var again = await SendJsonAsync(HttpMethod.Post, "/api/v1/pair", new PairRequest(code, "Someone else"));

        mistyped.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        mistyped.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem().Name.ShouldBe("Pixel 8");
    }

    [Fact]
    public async Task An_expired_code_is_forbidden()
    {
        var code = _pc.PairingCode;
        _pc.Clock.Advance(TimeSpan.FromMinutes(5));

        using var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/pair", new PairRequest(code, "Pixel 8"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_phone_unpaired_on_the_pc_is_shut_out()
    {
        await PairAsync();
        using var before = await GetAsync("/api/v1/movies");
        var device = (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem();

        await _pc.Phone.UnpairAsync(device.Id, Ct);

        using var after = await GetAsync("/api/v1/movies");
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_movie_added_over_http_is_in_the_pcs_collection_and_reads_back_as_named_enums()
    {
        await PairAsync();

        using var added = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat);

        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(added);
        json.GetProperty("status").GetString().ShouldBe("Saved");
        var movie = json.GetProperty("movie");
        movie.GetProperty("title").GetString().ShouldBe("Heat");
        movie.GetProperty("formats")[0].GetProperty("format").GetString().ShouldBe("Uhd4K");
        movie.GetProperty("formats")[0].GetProperty("upc").GetString().ShouldBe(HeatUpc);

        var onPc = (await _pc.Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        using var fetched = await GetAsync($"/api/v1/movies/{onPc.Id}");
        Wire.Json(await ReadAsync<MovieDto>(fetched)).ShouldBe(Wire.Json(await _pc.Api.GetMovieAsync(onPc.Id, Ct)));
    }

    [Fact]
    public async Task What_a_phone_saves_is_announced_on_the_pc_so_its_open_screens_can_show_it()
    {
        await PairAsync();
        var changes = 0;
        _pc.Api.Changed += () => changes++;

        using var added = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat);
        using var refused = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat with { Title = "" });
        using var listed = await GetAsync("/api/v1/movies");

        changes.ShouldBe(1);
    }

    [Fact]
    public async Task The_movie_list_takes_its_search_and_filters_from_the_query()
    {
        await PairAsync();
        await _pc.Api.AddMovieAsync(Heat, Ct);
        await _pc.Api.AddMovieAsync(new() { Title = "Alien", Year = 1979, Formats = [new(MovieFormatKind.Dvd)] }, Ct);

        using var all = await GetAsync("/api/v1/movies?sort=Title");
        using var dvds = await GetAsync("/api/v1/movies?format=Dvd");
        using var searched = await GetAsync("/api/v1/movies?search=hea");

        (await ReadAsync<MovieListResponse>(all)).Movies.Select(m => m.Title).ShouldBe(["Alien", "Heat"]);
        (await ReadAsync<MovieListResponse>(dvds)).Movies.Select(m => m.Title).ShouldBe(["Alien"]);
        (await ReadAsync<MovieListResponse>(searched)).Movies.Select(m => m.Title).ShouldBe(["Heat"]);
    }

    [Fact]
    public async Task A_save_that_doesnt_go_through_says_why_in_the_status_and_the_body()
    {
        await PairAsync();
        var saved = (await _pc.Api.AddMovieAsync(Heat, Ct)).Movie!;
        await _pc.Api.UpdateMovieAsync(saved.Id, saved.Version, Heat with { Year = 1996 }, Ct);

        using var invalid = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat with { Title = "" });
        using var owned = await SendJsonAsync(HttpMethod.Post, "/api/v1/movies", Heat);
        using var stale = await SendJsonAsync(HttpMethod.Put, $"/api/v1/movies/{saved.Id}?version={saved.Version}", Heat with { Year = 1997 });
        using var gone = await SendJsonAsync(HttpMethod.Put, $"/api/v1/movies/{Guid.NewGuid()}?version=1", Heat);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync<SaveMovieResponse>(invalid)).ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(SaveStatus.Invalid),
            r => r.Errors.ShouldHaveSingleItem().Field.ShouldBe("Title"));
        owned.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync<SaveMovieResponse>(owned)).ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(SaveStatus.AlreadyOwned),
            r => r.Movie!.Id.ShouldBe(saved.Id));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync<SaveMovieResponse>(stale)).Status.ShouldBe(SaveStatus.Conflict);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<SaveMovieResponse>(gone)).Status.ShouldBe(SaveStatus.NotFound);
    }

    [Fact]
    public async Task An_update_with_the_current_version_saves()
    {
        await PairAsync();
        var saved = (await _pc.Api.AddMovieAsync(Heat, Ct)).Movie!;

        using var response = await SendJsonAsync(HttpMethod.Put, $"/api/v1/movies/{saved.Id}?version={saved.Version}", Heat with { Rating = "R" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _pc.Api.GetMovieAsync(saved.Id, Ct))!.Rating.ShouldBe("R");
    }

    [Fact]
    public async Task Getting_or_deleting_what_isnt_there_is_not_found()
    {
        await PairAsync();
        var saved = (await _pc.Api.AddMovieAsync(Heat, Ct)).Movie!;

        using var unknown = await GetAsync($"/api/v1/movies/{Guid.NewGuid()}");
        using var notAnId = await GetAsync("/api/v1/movies/heat");
        using var deleted = await _http.DeleteAsync(new Uri($"/api/v1/movies/{saved.Id}", UriKind.Relative), Ct);
        using var deletedAgain = await _http.DeleteAsync(new Uri($"/api/v1/movies/{saved.Id}", UriKind.Relative), Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        notAnId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        deletedAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _pc.Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_body_that_isnt_the_expected_json_is_a_bad_request()
    {
        await PairAsync();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(new Uri("/api/v1/movies", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_film_is_looked_up_online_by_the_pc()
    {
        await PairAsync();
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb);

        using var response = await GetAsync("/api/v1/movies/lookup?q=heat%201995");

        var lookup = await ReadAsync<MovieLookupResponse>(response);
        lookup.Status.ShouldBe(LookupStatus.Found);
        lookup.Candidates.ShouldHaveSingleItem().TmdbId.ShouldBe(949);
    }

    [Fact]
    public async Task Comics_are_added_listed_by_shelf_and_fetched()
    {
        await PairAsync();

        using var added = await SendJsonAsync(HttpMethod.Post, "/api/v1/comics", Saga7);
        using var again = await SendJsonAsync(HttpMethod.Post, "/api/v1/comics", Saga7 with { Number = "" });
        using var series = await GetAsync("/api/v1/comics");
        using var issues = await GetAsync("/api/v1/comics?shelf=Issues&search=saga");

        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        var comic = (await ReadAsync<SaveComicResponse>(added)).Comic.ShouldNotBeNull();
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync<ComicListResponse>(series)).Runs.ShouldHaveSingleItem().Title.ShouldBe("Saga (2012)");
        (await ReadAsync<ComicListResponse>(issues)).Comics.ShouldHaveSingleItem().Id.ShouldBe(comic.Id);

        using var fetched = await GetAsync($"/api/v1/comics/{comic.Id}");
        Wire.Json(await ReadAsync<ComicDto>(fetched)).ShouldBe(Wire.Json(await _pc.Api.GetComicAsync(comic.Id, Ct)));
        using var deleted = await _http.DeleteAsync(new Uri($"/api/v1/comics/{comic.Id}", UriKind.Relative), Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_comic_is_looked_up_online_by_the_pc()
    {
        await PairAsync();
        _pc.Online.Metron.Add(TestComics.Saga7);

        using var response = await GetAsync("/api/v1/comics/lookup?q=saga%207");

        (await ReadAsync<ComicLookupResponse>(response)).Candidates.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #7");
    }

    [Fact]
    public async Task A_scan_is_looked_up_as_what_its_code_says_unless_the_phone_says_otherwise()
    {
        await PairAsync();
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _pc.Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
        _pc.Online.Metron.Add(TestComics.Saga7);

        using var film = await SendJsonAsync(HttpMethod.Post, "/api/v1/scans", new ScanRequest(HeatUpc));
        using var comic = await SendJsonAsync(HttpMethod.Post, "/api/v1/scans", new ScanRequest(TestComics.Saga7.Barcode!));
        using var forced = await SendJsonAsync(HttpMethod.Post, "/api/v1/scans", new ScanRequest(HeatUpc, ProductKind.Comic));

        (await ReadAsync<ScanResponse>(film)).ShouldSatisfyAllConditions(
            s => s.Kind.ShouldBe(ProductKind.Movie),
            s => s.Comic.ShouldBeNull(),
            s => s.Movie!.Format.ShouldBe(MovieFormatKind.Uhd4K),
            s => s.Movie!.Candidates.ShouldHaveSingleItem().Title.ShouldBe("Heat"));
        (await ReadAsync<ScanResponse>(comic)).ShouldSatisfyAllConditions(
            s => s.Kind.ShouldBe(ProductKind.Comic),
            s => s.Movie.ShouldBeNull(),
            s => s.Comic!.Candidates.ShouldHaveSingleItem().Details.Series.ShouldBe("Saga"));
        (await ReadAsync<ScanResponse>(forced)).Kind.ShouldBe(ProductKind.Comic);
    }

    [Fact]
    public async Task A_stored_cover_is_served_as_an_image_the_phone_may_keep()
    {
        await PairAsync();
        await _pc.Online.Covers.SaveAsync("abc-heat.jpg", [1, 2, 3], Ct);
        await _pc.Online.Covers.SaveAsync("abc-saga.png", [4], Ct);

        using var jpeg = await GetAsync("/api/v1/covers/abc-heat.jpg");
        using var png = await GetAsync("/api/v1/covers/abc-saga.png");
        using var missing = await GetAsync("/api/v1/covers/nope.jpg");

        jpeg.StatusCode.ShouldBe(HttpStatusCode.OK);
        jpeg.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
        jpeg.Headers.CacheControl.ShouldNotBeNull().MaxAge.ShouldBe(TimeSpan.FromDays(365));
        (await jpeg.Content.ReadAsByteArrayAsync(Ct)).ShouldBe([1, 2, 3]);
        png.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
