using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>The phone's view of the collection is the PC's own, call for call, over the web API.</summary>
public sealed class HttpCollectionApiTests : IAsyncLifetime
{
    private const string HeatUpc = "786936894424";

    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The collection as the phone sees it.</summary>
    private ICollectionApi Phone => _phone.Api;

    /// <summary>The collection as the PC's own screens see it.</summary>
    private ICollectionApi Pc => _pc.Api;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Vincent Hanna")],
        Crew = [new("Michael Mann", "Screenplay", "Writing"), new("Dante Spinotti", "Director of Photography", "Camera")],
        Genres = ["Crime", "Drama"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "A detective and a thief.",
        TmdbId = 949,
        TrailerYouTubeKey = "0xbBLJ1WGwQ",
        Formats = [new(MovieFormatKind.Uhd4K, Upc: HeatUpc), new(MovieFormatKind.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")],
    };

    private static SaveMovieRequest Alien => new() { Title = "Alien", Year = 1979, Genres = ["Horror"], Formats = [new(MovieFormatKind.Dvd)] };

    private static SaveComicRequest Saga(string number) => new()
    {
        Series = "Saga",
        SeriesYear = 2012,
        Number = number,
        Publisher = "Image",
        CoverDate = new DateOnly(2012, 11, 1),
        Writers = ["Brian K. Vaughan"],
        Artists = ["Fiona Staples"],
        MetronSeriesId = 1234,
    };

    private static SaveComicRequest SagaTrade => new()
    {
        Format = ComicFormatKind.TradePaperback,
        Title = "Saga, Volume One",
        Series = "Saga",
        Number = "1",
        Isbn = "9781607066019",
    };

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _phone = new TestPhone(_pc);
        await _phone.PairAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
    }

    [Fact]
    public async Task A_movie_added_on_the_phone_is_saved_on_the_pc_with_every_field()
    {
        var response = await Phone.AddMovieAsync(Heat, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var onPc = (await Pc.GetMovieAsync(response.Movie!.Id, Ct)).ShouldNotBeNull();
        Wire.Json(response.Movie).ShouldBe(Wire.Json(onPc));
        Wire.Json(await Phone.GetMovieAsync(onPc.Id, Ct)).ShouldBe(Wire.Json(onPc));
        onPc.Formats.Select(f => (f.Format, f.Upc, f.FilePath)).ShouldBe(
        [
            (MovieFormatKind.Uhd4K, HeatUpc, null),
            (MovieFormatKind.Digital, null, @"D:\Movies\Heat (1995).mkv"),
        ]);
    }

    public static TheoryData<MovieListRequest> MovieLists =>
    [
        new MovieListRequest(),
        new MovieListRequest { Search = "hea" },
        new MovieListRequest { Search = "Mann & co" },
        new MovieListRequest { Format = MovieFormatKind.Dvd },
        new MovieListRequest { Genre = "Crime" },
        new MovieListRequest { Decade = 1970, Sort = MovieSortOrder.Title },
        new MovieListRequest { Sort = MovieSortOrder.Year },
        new MovieListRequest { Search = "nothing like this" },
    ];

    [Theory]
    [MemberData(nameof(MovieLists))]
    public async Task The_phone_lists_movies_exactly_as_the_pc_does(MovieListRequest request)
    {
        await Pc.AddMovieAsync(Heat, Ct);
        _pc.Clock.Advance(TimeSpan.FromMinutes(1));
        await Pc.AddMovieAsync(Alien, Ct);

        var onPhone = await Phone.ListMoviesAsync(request, Ct);

        Wire.Json(onPhone).ShouldBe(Wire.Json(await Pc.ListMoviesAsync(request, Ct)));
    }

    [Fact]
    public async Task What_the_pc_refuses_to_save_comes_back_to_the_phone_with_the_reason()
    {
        var saved = (await Pc.AddMovieAsync(Heat, Ct)).Movie!;
        await Pc.UpdateMovieAsync(saved.Id, saved.Version, Heat with { Year = 1996 }, Ct);

        var invalid = await Phone.AddMovieAsync(Heat with { Title = " ", Formats = [new(MovieFormatKind.BluRay, Upc: "123")] }, Ct);
        var owned = await Phone.AddMovieAsync(Heat, Ct);
        var stale = await Phone.UpdateMovieAsync(saved.Id, saved.Version, Heat with { Year = 1997 }, Ct);
        var gone = await Phone.UpdateMovieAsync(Guid.NewGuid(), 1, Heat, Ct);

        invalid.Status.ShouldBe(SaveStatus.Invalid);
        invalid.Errors.Select(e => e.Field).ShouldBe(["Title", "BluRay.Upc"], ignoreOrder: true);
        owned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        owned.Movie!.Id.ShouldBe(saved.Id);
        stale.Status.ShouldBe(SaveStatus.Conflict);
        gone.Status.ShouldBe(SaveStatus.NotFound);
        (await Pc.GetMovieAsync(saved.Id, Ct))!.Year.ShouldBe(1996);
    }

    [Fact]
    public async Task The_phone_can_update_and_delete_a_movie()
    {
        var saved = (await Pc.AddMovieAsync(Heat, Ct)).Movie!;

        var updated = await Phone.UpdateMovieAsync(saved.Id, saved.Version, Heat with { Rating = "18" }, Ct);

        updated.Status.ShouldBe(SaveStatus.Saved);
        (await Pc.GetMovieAsync(saved.Id, Ct))!.Rating.ShouldBe("18");
        (await Phone.DeleteMovieAsync(saved.Id, Ct)).ShouldBeTrue();
        (await Phone.DeleteMovieAsync(saved.Id, Ct)).ShouldBeFalse();
        (await Phone.GetMovieAsync(saved.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_phone_announces_what_it_saved_or_deleted_and_not_what_was_refused()
    {
        var changes = 0;
        Phone.Changed += () => changes++;

        var saved = (await Phone.AddMovieAsync(Heat, Ct)).Movie!;
        changes.ShouldBe(1);

        await Phone.AddMovieAsync(Heat, Ct);
        await Phone.AddMovieAsync(Heat with { Title = "" }, Ct);
        await Phone.UpdateMovieAsync(saved.Id, saved.Version + 1, Heat, Ct);
        await Phone.DeleteMovieAsync(Guid.NewGuid(), Ct);
        await Phone.ListMoviesAsync(new(), Ct);
        changes.ShouldBe(1);

        await Phone.UpdateMovieAsync(saved.Id, saved.Version, Heat with { Rating = "18" }, Ct);
        await Phone.DeleteMovieAsync(saved.Id, Ct);
        await Phone.AddComicAsync(Saga("7"), Ct);
        changes.ShouldBe(4);
    }

    [Fact]
    public async Task The_pc_looks_films_up_online_for_the_phone()
    {
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _pc.Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");

        var byTitle = await Phone.LookupMovieAsync("heat 1995", Ct);
        var byCode = await Phone.LookupMovieAsync(HeatUpc, Ct);

        Wire.Json(byTitle).ShouldBe(Wire.Json(await Pc.LookupMovieAsync("heat 1995", Ct)));
        byTitle.Candidates.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        byCode.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(LookupStatus.Found),
            r => r.Upc.ShouldBe(HeatUpc),
            r => r.Format.ShouldBe(MovieFormatKind.Uhd4K));
    }

    [Fact]
    public async Task A_lookup_the_pc_cant_do_says_why()
    {
        _pc.Online.Tmdb.FailWith = Application.Metadata.MetadataProblem.NotConfigured;

        var response = await Phone.LookupMovieAsync("heat", Ct);

        response.Status.ShouldBe(LookupStatus.NotConfigured);
        response.FailedService.ShouldBe("TMDB");
    }

    public static TheoryData<ComicListRequest> ComicLists =>
    [
        new ComicListRequest(),
        new ComicListRequest { Search = "saga" },
        new ComicListRequest { Shelf = ComicShelfKind.Issues, Sort = ComicSortOrder.RecentlyAdded },
        new ComicListRequest { Shelf = ComicShelfKind.Collected },
        new ComicListRequest { Shelf = ComicShelfKind.KeyIssues },
        new ComicListRequest { Publisher = "Image" },
    ];

    [Theory]
    [MemberData(nameof(ComicLists))]
    public async Task The_phone_lists_comics_exactly_as_the_pc_does(ComicListRequest request)
    {
        _pc.Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7"]));
        await Pc.AddComicAsync(Saga("2"), Ct);
        _pc.Clock.Advance(TimeSpan.FromMinutes(1));
        await Pc.AddComicAsync(Saga("7") with { IsKeyIssue = true, KeyNote = "First Lying Cat" }, Ct);
        await Pc.AddComicAsync(SagaTrade, Ct);

        var onPhone = await Phone.ListComicsAsync(request, Ct);

        Wire.Json(onPhone).ShouldBe(Wire.Json(await Pc.ListComicsAsync(request, Ct)));
    }

    [Fact]
    public async Task A_comic_added_on_the_phone_is_saved_on_the_pc_and_the_same_copy_twice_is_turned_down()
    {
        var response = await Phone.AddComicAsync(Saga("7") with { Barcode = "70985301744800711", Variant = "Cover B" }, Ct);
        var again = await Phone.AddComicAsync(Saga("7") with { Barcode = "70985301744800711" }, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var onPc = (await Pc.GetComicAsync(response.Comic!.Id, Ct)).ShouldNotBeNull();
        Wire.Json(await Phone.GetComicAsync(onPc.Id, Ct)).ShouldBe(Wire.Json(onPc));
        onPc.Barcode.ShouldBe("70985301744800711");
        onPc.CoverDate.ShouldBe(new DateOnly(2012, 11, 1));
        again.Status.ShouldBe(SaveStatus.AlreadyOwned);
        again.Comic!.Id.ShouldBe(onPc.Id);
    }

    [Fact]
    public async Task The_phone_can_update_and_delete_a_comic()
    {
        var saved = (await Pc.AddComicAsync(Saga("7"), Ct)).Comic!;

        var updated = await Phone.UpdateComicAsync(saved.Id, saved.Version, Saga("7") with { KeyNote = "Signed", IsKeyIssue = true }, Ct);
        var stale = await Phone.UpdateComicAsync(saved.Id, saved.Version, Saga("8"), Ct);

        updated.Status.ShouldBe(SaveStatus.Saved);
        stale.Status.ShouldBe(SaveStatus.Conflict);
        (await Pc.GetComicAsync(saved.Id, Ct))!.KeyNote.ShouldBe("Signed");
        (await Phone.DeleteComicAsync(saved.Id, Ct)).ShouldBeTrue();
        (await Phone.DeleteComicAsync(saved.Id, Ct)).ShouldBeFalse();
        (await Phone.GetComicAsync(saved.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_pc_looks_comics_up_online_for_the_phone()
    {
        _pc.Online.Metron.Add(TestComics.Saga7);

        var response = await Phone.LookupComicAsync("Saga (2012) #7", Ct);

        Wire.Json(response).ShouldBe(Wire.Json(await Pc.LookupComicAsync("Saga (2012) #7", Ct)));
        response.Candidates.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #7");
    }

    [Fact]
    public async Task A_scanned_code_comes_back_as_a_film_or_a_comic()
    {
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _pc.Online.Barcodes.Add(HeatUpc, "Heat [Blu-ray]");
        _pc.Online.Metron.Add(TestComics.Saga7);

        var film = await Phone.LookupScanAsync(new(HeatUpc), Ct);
        var comic = await Phone.LookupScanAsync(new(TestComics.Saga7.Barcode!), Ct);
        var toldComic = await Phone.LookupScanAsync(new(HeatUpc, ProductKind.Comic), Ct);

        film.Kind.ShouldBe(ProductKind.Movie);
        film.Movie!.Format.ShouldBe(MovieFormatKind.BluRay);
        film.Movie.Candidates.ShouldHaveSingleItem().TmdbId.ShouldBe(949);
        comic.Kind.ShouldBe(ProductKind.Comic);
        comic.Comic!.Candidates.ShouldHaveSingleItem().Details.MetronId.ShouldBe(4521);
        toldComic.Kind.ShouldBe(ProductKind.Comic);
        toldComic.Movie.ShouldBeNull();
        Wire.Json(film).ShouldBe(Wire.Json(await Pc.LookupScanAsync(new(HeatUpc), Ct)));
    }
}
