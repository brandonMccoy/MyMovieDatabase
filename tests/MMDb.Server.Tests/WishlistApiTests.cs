using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Local;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The wishlist as the PC's screens use it and as a paired phone reaches it over the web API.</summary>
public sealed class WishlistApiTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ICollectionApi Api => _pc.Api;

    private static WantMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        TmdbId = 949,
        TrailerYouTubeKey = "0xbBLJ1WGwQ",
        PosterPath = "/heat.jpg",
        Format = MovieFormatKind.Uhd4K,
    };

    private static SaveMovieRequest HeatOn(params MovieFormatKind[] formats) =>
        new() { Title = "Heat", Year = 1995, TmdbId = 949, Formats = [.. formats.Select(f => new FormatRequest(f))] };

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

    private Task<HttpResponseMessage> PostAsync<T>(string path, T body) =>
        _http.PostAsync(new Uri(path, UriKind.Relative), JsonContent.Create(body, options: ShelfJson.Options), Ct);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    [Fact]
    public async Task A_wanted_film_is_on_the_wishlist_with_its_cover_trailer_and_what_is_owned_of_it()
    {
        await Api.AddMovieAsync(HeatOn(MovieFormatKind.Dvd), Ct);

        var response = await Api.WantMovieAsync(Heat, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var wishlist = await Api.ListWishlistAsync(cancellationToken: Ct);
        wishlist.MovieCount.ShouldBe(1);
        var item = wishlist.Movies.ShouldHaveSingleItem();
        Wire.Json(item).ShouldBe(Wire.Json(response.Movie));
        item.ShouldSatisfyAllConditions(
            i => i.Title.ShouldBe("Heat"),
            i => i.Year.ShouldBe(1995),
            i => i.Directors.ShouldBe(["Michael Mann"]),
            i => i.Format.ShouldBe(MovieFormatKind.Uhd4K),
            i => i.OwnedFormats.ShouldBe([MovieFormatKind.Dvd]),
            i => i.CoverUrl.ShouldNotBeNull().ToString().ShouldStartWith(TestPc.CoverBase.ToString()),
            i => i.TrailerUrl.ToString().ShouldBe("https://www.youtube.com/watch?v=0xbBLJ1WGwQ"),
            i => i.AmazonUrl.ShouldNotBeNull().AbsoluteUri.ShouldBe("https://www.amazon.com/s?k=Heat%201995%204K%20UHD&i=movies-tv"),
            i => i.AddedAt.ShouldBe(TestPc.Start));
    }

    [Fact]
    public async Task One_wishlist_item_is_fetched_and_a_films_wanted_format_changed()
    {
        await Api.AddMovieAsync(HeatOn(MovieFormatKind.Dvd), Ct);
        var film = (await Api.WantMovieAsync(Heat, Ct)).Movie!;
        var comic = (await Api.WantComicAsync(Saga7, Ct)).Comic!;
        var changes = 0;
        Api.Changed += () => changes++;

        Wire.Json(await Api.GetWishlistMovieAsync(film.Id, Ct)).ShouldBe(Wire.Json(film));
        Wire.Json(await Api.GetWishlistComicAsync(comic.Id, Ct)).ShouldBe(Wire.Json(comic));
        comic.Number.ShouldBe("7");
        (await Api.GetWishlistMovieAsync(comic.Id, Ct)).ShouldBeNull();
        (await Api.GetWishlistComicAsync(film.Id, Ct)).ShouldBeNull();

        var changed = await Api.ChangeWishlistFormatAsync(film.Id, MovieFormatKind.BluRay, Ct);
        var owned = await Api.ChangeWishlistFormatAsync(film.Id, MovieFormatKind.Dvd, Ct);
        var gone = await Api.ChangeWishlistFormatAsync(Guid.NewGuid(), MovieFormatKind.Dvd, Ct);

        changed.Status.ShouldBe(SaveStatus.Saved);
        changed.Movie.ShouldNotBeNull().ShouldSatisfyAllConditions(
            m => m.Format.ShouldBe(MovieFormatKind.BluRay),
            m => m.OwnedFormats.ShouldBe([MovieFormatKind.Dvd]),
            m => m.CoverUrl.ShouldBe(film.CoverUrl));
        owned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        gone.Status.ShouldBe(SaveStatus.NotFound);
        changes.ShouldBe(1);
        (await Api.GetWishlistMovieAsync(film.Id, Ct))!.Format.ShouldBe(MovieFormatKind.BluRay);
    }

    [Fact]
    public async Task One_wishlist_item_is_read_and_its_format_changed_over_http()
    {
        await PairAsync();
        var film = (await Api.WantMovieAsync(Heat, Ct)).Movie!;
        var comic = (await Api.WantComicAsync(Saga7, Ct)).Comic!;

        using var gotFilm = await GetAsync($"/api/v1/wishlist/movies/{film.Id}");
        using var gotComic = await GetAsync($"/api/v1/wishlist/comics/{comic.Id}");
        using var noFilm = await GetAsync($"/api/v1/wishlist/movies/{comic.Id}");
        using var noComic = await GetAsync($"/api/v1/wishlist/comics/{film.Id}");
        using var changed = await _http.PutAsync(new Uri($"/api/v1/wishlist/movies/{film.Id}/format", UriKind.Relative), JsonContent.Create(new ChangeWishFormatRequest(MovieFormatKind.Dvd), options: ShelfJson.Options), Ct);
        using var gone = await _http.PutAsync(new Uri($"/api/v1/wishlist/movies/{comic.Id}/format", UriKind.Relative), JsonContent.Create(new ChangeWishFormatRequest(MovieFormatKind.Dvd), options: ShelfJson.Options), Ct);

        gotFilm.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(gotFilm)).GetProperty("format").GetString().ShouldBe("Uhd4K");
        gotComic.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(gotComic)).GetProperty("displayName").GetString().ShouldBe("Saga #7");
        noFilm.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noComic.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(changed)).GetProperty("movie").GetProperty("format").GetString().ShouldBe("Dvd");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task What_cannot_be_wanted_says_why()
    {
        var owned = (await Api.AddMovieAsync(HeatOn(MovieFormatKind.Uhd4K), Ct)).Movie!;

        var alreadyOwned = await Api.WantMovieAsync(Heat, Ct);
        var invalid = await Api.WantMovieAsync(Heat with { Title = "", TmdbId = null }, Ct);

        alreadyOwned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        alreadyOwned.Owned!.Id.ShouldBe(owned.Id);
        invalid.Status.ShouldBe(SaveStatus.Invalid);
        invalid.Errors.ShouldHaveSingleItem().Field.ShouldBe("Title");
    }

    [Fact]
    public async Task Whatever_changes_the_wishlist_is_announced_and_nothing_else_is()
    {
        var changes = 0;
        Api.Changed += () => changes++;

        var item = (await Api.WantMovieAsync(Heat, Ct)).Movie!;
        changes.ShouldBe(1);

        await Api.WantMovieAsync(Heat with { Title = "" }, Ct);
        await Api.ListWishlistAsync("heat", Ct);
        await Api.LookupWishlistMovieAsync(item.Id, Ct);
        await Api.RemoveWishlistMovieAsync(Guid.NewGuid(), Ct);
        changes.ShouldBe(1, "refused wishes and reads change nothing");

        (await Api.RemoveWishlistMovieAsync(item.Id, Ct)).ShouldBeTrue();
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task A_lookup_tells_which_film_is_wanted_and_saving_it_on_that_format_grants_the_wish()
    {
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        var item = (await Api.WantMovieAsync(Heat, Ct)).Movie!;

        var found = await Api.LookupMovieAsync("heat", Ct);
        var gotIt = (await Api.LookupWishlistMovieAsync(item.Id, Ct)).ShouldNotBeNull();
        await Api.AddMovieAsync(HeatOn(MovieFormatKind.Uhd4K), Ct);

        found.Candidates.Select(c => c.Wish).ShouldBe([new MovieWishDto(item.Id, MovieFormatKind.Uhd4K, "Heat", 1995), null]);
        gotIt.Wish.ShouldBe(new MovieWishDto(item.Id, MovieFormatKind.Uhd4K, "Heat", 1995));
        gotIt.Candidates.ShouldHaveSingleItem().TmdbId.ShouldBe(949);
        (await Api.ListWishlistAsync(cancellationToken: Ct)).MovieCount.ShouldBe(0);
        (await Api.LookupWishlistMovieAsync(item.Id, Ct)).ShouldBeNull();
    }

    private static WantComicRequest Saga7 => new()
    {
        Series = "Saga",
        SeriesYear = 2012,
        Number = "7",
        Publisher = "Image",
        MetronId = 4521,
        MetronSeriesId = 1234,
        CoverSource = "https://static.metron.cloud/media/issue/saga-7.jpg",
    };

    private static SaveComicRequest SagaIssue(string number) => new() { Series = "Saga", SeriesYear = 2012, Number = number, Publisher = "Image", MetronSeriesId = 1234 };

    private async Task OwnSagaAsync(params string[] numbers)
    {
        _pc.Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));
        foreach (var number in numbers)
        {
            (await Api.AddComicAsync(SagaIssue(number), Ct)).Status.ShouldBe(SaveStatus.Saved);
        }
    }

    [Fact]
    public async Task A_wanted_comic_is_on_the_wishlist_with_its_cover_and_a_copy_saved_takes_it_off()
    {
        var response = await Api.WantComicAsync(Saga7, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var wishlist = await Api.ListWishlistAsync(cancellationToken: Ct);
        wishlist.ComicCount.ShouldBe(1);
        wishlist.MovieCount.ShouldBe(0);
        var item = wishlist.Comics.ShouldHaveSingleItem();
        Wire.Json(item).ShouldBe(Wire.Json(response.Comic));
        item.ShouldSatisfyAllConditions(
            i => i.DisplayName.ShouldBe("Saga #7"),
            i => i.Format.ShouldBe(ComicFormatKind.SingleIssue),
            i => i.Series.ShouldBe("Saga"),
            i => i.SeriesYear.ShouldBe(2012),
            i => i.Publisher.ShouldBe("Image"),
            i => i.FromMissing.ShouldBeFalse(),
            i => i.CoverUrl.ShouldNotBeNull().ToString().ShouldStartWith(TestPc.CoverBase.ToString()),
            i => i.AddedAt.ShouldBe(TestPc.Start));

        await Api.AddComicAsync(SagaIssue("7") with { Variant = "Cover B" }, Ct);

        (await Api.ListWishlistAsync(cancellationToken: Ct)).ComicCount.ShouldBe(0);
    }

    [Fact]
    public async Task Missing_issues_are_wanted_one_item_each_and_the_library_says_they_are()
    {
        await OwnSagaAsync("1", "6");
        var changes = 0;
        Api.Changed += () => changes++;

        var response = await Api.WantMissingIssuesAsync(new(1234, "2", "5"), Ct);
        var unknown = await Api.WantMissingIssuesAsync(new(999, "2", "5"), Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        response.Comic.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.DisplayName.ShouldBe("Saga #2"),
            c => c.FromMissing.ShouldBeTrue());
        (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.Select(c => c.DisplayName).ShouldBe(["Saga #2", "Saga #3", "Saga #4", "Saga #5"]);
        unknown.Status.ShouldBe(SaveStatus.NotFound);
        changes.ShouldBe(1);
        var run = (await Api.ListComicsAsync(new(), Ct)).Runs.ShouldHaveSingleItem();
        run.MetronSeriesId.ShouldBe(1234);
        run.Entries.Where(e => e.IsMissing).Select(e => (e.Number, e.Through, e.Wanted)).ShouldBe([("2", "5", true)]);
    }

    [Fact]
    public async Task Covers_that_arrive_late_are_announced_so_open_screens_show_them()
    {
        await OwnSagaAsync("1", "6");
        _pc.Online.Metron.Add(TestComics.SagaIssue("4"));
        _pc.Online.ComicCovers.Offline = true;
        await Api.WantMissingIssuesAsync(new(1234, "4"), Ct);
        var pc = (InProcessCollectionApi)Api;
        var changes = 0;
        Api.Changed += () => changes++;

        (await pc.FetchMissingWishlistCoversAsync(Ct)).ShouldBe(0);
        changes.ShouldBe(0);
        _pc.Online.ComicCovers.Offline = false;

        (await pc.FetchMissingWishlistCoversAsync(Ct)).ShouldBe(1);

        changes.ShouldBe(1);
        (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.ShouldHaveSingleItem().CoverUrl.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_comic_lookup_tells_what_is_wanted_and_got_it_finds_the_issue()
    {
        _pc.Online.Metron.Add(TestComics.Saga7);
        var item = (await Api.WantComicAsync(Saga7, Ct)).Comic!;
        var wish = new ComicWishDto(item.Id, "Saga #7", "Saga", 2012, "7");

        var found = await Api.LookupComicAsync("saga 7", Ct);
        var gotIt = (await Api.LookupWishlistComicAsync(item.Id, Ct)).ShouldNotBeNull();

        found.Candidates.ShouldHaveSingleItem().Wish.ShouldBe(wish);
        found.Wish.ShouldBeNull();
        gotIt.Wish.ShouldBe(wish);
        gotIt.Candidates.ShouldHaveSingleItem().Details.MetronId.ShouldBe(4521);
        (await Api.RemoveWishlistComicAsync(item.Id, Ct)).ShouldBeTrue();
        (await Api.RemoveWishlistComicAsync(item.Id, Ct)).ShouldBeFalse();
        (await Api.LookupWishlistComicAsync(item.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Comics_are_wanted_and_removed_over_http()
    {
        await PairAsync();
        await OwnSagaAsync("1", "6");

        using var wanted = await PostAsync("/api/v1/wishlist/comics", Saga7 with { Number = "8", MetronId = null });
        using var invalid = await PostAsync("/api/v1/wishlist/comics", Saga7 with { Number = null });
        using var missing = await PostAsync("/api/v1/wishlist/comics/missing", new WantMissingRequest(1234, "2", "5"));
        using var unknown = await PostAsync("/api/v1/wishlist/comics/missing", new WantMissingRequest(999, "2"));

        wanted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await JsonAsync(wanted)).GetProperty("comic");
        item.GetProperty("displayName").GetString().ShouldBe("Saga #8");
        item.GetProperty("format").GetString().ShouldBe("SingleIssue");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(missing)).GetProperty("comic").GetProperty("displayName").GetString().ShouldBe("Saga #2");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var id = item.GetProperty("id").GetGuid();
        using var lookup = await GetAsync($"/api/v1/wishlist/comics/{id}/lookup");
        using var removed = await _http.DeleteAsync(new Uri($"/api/v1/wishlist/comics/{id}", UriKind.Relative), Ct);
        using var again = await _http.DeleteAsync(new Uri($"/api/v1/wishlist/comics/{id}", UriKind.Relative), Ct);
        using var gone = await GetAsync($"/api/v1/wishlist/comics/{id}/lookup");

        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.Select(c => c.DisplayName).ShouldBe(["Saga #2", "Saga #3", "Saga #4", "Saga #5"]);
    }

    [Theory]
    [InlineData("GET", "/api/v1/wishlist")]
    [InlineData("GET", "/api/v1/wishlist/movies/0198c1de-0000-7000-8000-000000000000")]
    [InlineData("GET", "/api/v1/wishlist/comics/0198c1de-0000-7000-8000-000000000000")]
    [InlineData("POST", "/api/v1/wishlist/comics")]
    [InlineData("POST", "/api/v1/wishlist/comics/missing")]
    [InlineData("DELETE", "/api/v1/wishlist/comics/0198c1de-0000-7000-8000-000000000000")]
    [InlineData("GET", "/api/v1/wishlist/comics/0198c1de-0000-7000-8000-000000000000/lookup")]
    [InlineData("POST", "/api/v1/wishlist/movies")]
    [InlineData("DELETE", "/api/v1/wishlist/movies/0198c1de-0000-7000-8000-000000000000")]
    [InlineData("GET", "/api/v1/wishlist/movies/0198c1de-0000-7000-8000-000000000000/lookup")]
    public async Task The_wishlist_needs_a_paired_device(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method == "POST")
        {
            request.Content = path.EndsWith("/missing", StringComparison.Ordinal) ? JsonContent.Create(new WantMissingRequest(1234, "2"), options: ShelfJson.Options)
                : path.EndsWith("/comics", StringComparison.Ordinal) ? JsonContent.Create(Saga7, options: ShelfJson.Options)
                : JsonContent.Create(Heat, options: ShelfJson.Options);
        }

        using var response = await _http.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var wishlist = await Api.ListWishlistAsync(cancellationToken: Ct);
        (wishlist.MovieCount + wishlist.ComicCount).ShouldBe(0);
    }

    [Fact]
    public async Task A_film_wanted_over_http_is_on_the_pcs_wishlist_and_reads_back_as_named_enums()
    {
        await PairAsync();

        using var wanted = await PostAsync("/api/v1/wishlist/movies", Heat);
        using var listed = await GetAsync("/api/v1/wishlist?search=mann");

        wanted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(wanted);
        json.GetProperty("status").GetString().ShouldBe("Saved");
        json.GetProperty("movie").GetProperty("format").GetString().ShouldBe("Uhd4K");
        var list = await JsonAsync(listed);
        list.GetProperty("movieCount").GetInt32().ShouldBe(1);
        list.GetProperty("movies")[0].GetProperty("title").GetString().ShouldBe("Heat");
        (await Api.ListWishlistAsync(cancellationToken: Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
    }

    [Fact]
    public async Task A_wish_that_doesnt_go_through_says_why_in_the_status_and_the_body()
    {
        await PairAsync();
        await Api.AddMovieAsync(HeatOn(MovieFormatKind.Uhd4K), Ct);

        using var invalid = await PostAsync("/api/v1/wishlist/movies", Heat with { Title = "", TmdbId = null });
        using var owned = await PostAsync("/api/v1/wishlist/movies", Heat);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(invalid)).GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("Title");
        owned.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(owned)).GetProperty("status").GetString().ShouldBe("AlreadyOwned");
    }

    [Fact]
    public async Task Removing_or_looking_up_what_isnt_wanted_is_not_found()
    {
        await PairAsync();
        var item = (await Api.WantMovieAsync(Heat, Ct)).Movie!;

        using var removed = await _http.DeleteAsync(new Uri($"/api/v1/wishlist/movies/{item.Id}", UriKind.Relative), Ct);
        using var again = await _http.DeleteAsync(new Uri($"/api/v1/wishlist/movies/{item.Id}", UriKind.Relative), Ct);
        using var lookup = await GetAsync($"/api/v1/wishlist/movies/{item.Id}/lookup");

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        lookup.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
