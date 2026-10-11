using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>
/// Saving on the phone: it shows in the phone's copy at once, with or without the PC, and reaches the PC when
/// that is next in reach, where the PC's own rules decide.
/// </summary>
public sealed class PhoneShelfTests : IAsyncLifetime
{
    private const string HeatUpc = "786936894424";

    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ICollectionApi Pc => _pc.Api;

    /// <summary>What the phone's screens use.</summary>
    private ICollectionApi Shelf => _phone.Shelf;

    private ShelfSync Sync => _phone.Sync;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Lt. Vincent Hanna")],
        Genres = ["Crime"],
        TmdbId = 949,
        PosterPath = "/heat.jpg",
        Formats = [new(MovieFormatKind.BluRay, Upc: HeatUpc)],
    };

    private static SaveComicRequest Saga(string number) => new() { Series = "Saga", SeriesYear = 2012, Number = number, Publisher = "Image", MetronSeriesId = 1234 };

    private static WantMovieRequest WantHeat => new() { Title = "Heat", Year = 1995, Directors = ["Michael Mann"], TmdbId = 949, PosterPath = "/heat.jpg", Format = MovieFormatKind.Uhd4K };

    private static WantMovieRequest WantDune => new() { Title = "Dune", Year = 2021, TmdbId = 438631, Format = MovieFormatKind.Uhd4K };

    private static WantComicRequest WantSaga9 => new() { Series = "Saga", SeriesYear = 2012, Number = "9", Publisher = "Image" };

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

    /// <summary>Leaves home: from here on the PC doesn't answer.</summary>
    private async Task GoAwayAsync()
    {
        await HomeAsync();
        _phone.Away = true;
    }

    /// <summary>Back in reach of the PC, and everything that follows from that has happened.</summary>
    private async Task HomeAsync()
    {
        _phone.Away = false;
        await Sync.SyncAsync(Ct);
        await Sync.Covers.WaitAsync(Ct);
    }

    private IReadOnlyList<string> SentToThePc() =>
        [.. _phone.Transport.Asked
            .Select(a => a[(a.IndexOf("/api/v1/", StringComparison.Ordinal) + "/api/v1/".Length)..])
            .Where(a => a is not ("pair" or "keys" or "session" or "sync" or "sync/rows") && !a.StartsWith("covers/", StringComparison.Ordinal))];

    private async Task ThePhoneShowsWhatThePcHasAsync()
    {
        Wire.Json(await Shelf.ListMoviesAsync(new(), Ct)).ShouldBe(Wire.Json(await Pc.ListMoviesAsync(new(), Ct)));
        Wire.Json(await Shelf.ListComicsAsync(new(), Ct)).ShouldBe(Wire.Json(await Pc.ListComicsAsync(new(), Ct)));
        Wire.Json(await Shelf.ListWishlistAsync(null, Ct)).ShouldBe(Wire.Json(await Pc.ListWishlistAsync(null, Ct)));
        Sync.Status.Waiting.ShouldBe(0);
    }

    [Fact]
    public async Task A_film_added_away_from_home_shows_on_the_phone_at_once_and_reaches_the_pc_later()
    {
        await GoAwayAsync();

        var saved = await Shelf.AddMovieAsync(Heat, Ct);

        var film = saved.Movie.ShouldNotBeNull();
        saved.Status.ShouldBe(SaveStatus.Saved);
        (await Shelf.ListMoviesAsync(new() { Search = "pacino" }, Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.BluRay]);
        (await Shelf.GetMovieAsync(film.Id, Ct)).ShouldNotBeNull().Overview.ShouldBe(film.Overview);
        Sync.Status.Waiting.ShouldBe(1);
        Sync.IsUnsent(film.Id).ShouldBeTrue();
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();

        await HomeAsync();

        var onPc = (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        (await Pc.GetMovieAsync(onPc.Id, Ct)).ShouldNotBeNull().ShouldSatisfyAllConditions(
            m => m.Title.ShouldBe("Heat"),
            m => m.Cast.ShouldHaveSingleItem().Name.ShouldBe("Al Pacino"),
            m => m.Formats.ShouldHaveSingleItem().Upc.ShouldBe(HeatUpc));
        await ThePhoneShowsWhatThePcHasAsync();
        Sync.IsUnsent(onPc.Id).ShouldBeFalse();
        Sync.Status.TurnedDown.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_home_what_is_saved_on_the_phone_goes_to_the_pc_by_itself()
    {
        await HomeAsync();

        await Shelf.AddMovieAsync(Heat, Ct);
        await Shelf.AddComicAsync(Saga("7"), Ct);

        // Whoever asks for a sync while one is running waits for it and for the one that follows.
        await HomeAsync();
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        (await Pc.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #7");
        await ThePhoneShowsWhatThePcHasAsync();
    }

    /// <summary>Lets time pass, a second at a time, until what waits on the clock is done.</summary>
    private async Task WhileTimePassesAsync(Task task)
    {
        for (var i = 0; i < 400 && !task.IsCompleted; i++)
        {
            _pc.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, Ct);
        }

        await task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task When_the_network_comes_back_the_phone_syncs_by_itself_and_keeps_trying_for_a_moment()
    {
        await GoAwayAsync();
        await Shelf.AddMovieAsync(Heat, Ct);
        await Sync.SyncAsync(Ct);

        // Wi-Fi is back, but the PC can't be reached over it for the first few seconds.
        var settling = Sync.NetworkChangedAsync(Ct);
        _pc.Clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(100, Ct);
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
        _phone.Away = false;
        await WhileTimePassesAsync(settling);

        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        Sync.Status.Waiting.ShouldBe(0);
        Sync.Status.SyncedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task On_a_network_the_pc_isnt_on_the_phone_gives_up_after_a_few_tries()
    {
        await GoAwayAsync();
        var before = _phone.Transport.Asked.Count;

        await WhileTimePassesAsync(Sync.NetworkChangedAsync(Ct));

        _phone.Transport.Asked.Count.ShouldBe(before + 3);
        _pc.Clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50, Ct);
        _phone.Transport.Asked.Count.ShouldBe(before + 3);
    }

    [Fact]
    public async Task A_newer_change_of_network_takes_over_from_one_still_trying()
    {
        await GoAwayAsync();
        var before = _phone.Transport.Asked.Count;

        var first = Sync.NetworkChangedAsync(Ct);
        var second = Sync.NetworkChangedAsync(Ct);
        await first.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        _phone.Away = false;
        await WhileTimePassesAsync(second);

        _phone.Transport.Asked.Skip(before).Count(a => a.EndsWith("/keys", StringComparison.Ordinal)).ShouldBe(1, "one try, by the newer one, and it got through");
    }

    [Fact]
    public async Task What_was_saved_away_from_home_is_still_waiting_after_the_app_was_closed()
    {
        await GoAwayAsync();
        await Shelf.AddMovieAsync(Heat, Ct);
        await Shelf.WantMovieAsync(WantDune, Ct);
        await Sync.SyncAsync(Ct);

        using var reopened = new TestPhone(_pc, restartOf: _phone);
        await reopened.StartAsync();

        reopened.Sync.Status.Waiting.ShouldBe(2);
        (await reopened.Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");

        reopened.Away = false;
        await reopened.Sync.SyncAsync(Ct);

        reopened.Sync.Status.Waiting.ShouldBe(0);
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        (await Pc.ListWishlistAsync(null, Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Dune");
    }

    [Fact]
    public async Task The_phone_looks_things_up_itself_away_from_home_and_knows_what_is_owned_and_wanted()
    {
        _phone.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        _phone.Online.Barcodes.Add("043396000001", "Heat (1986) [DVD]");
        await Pc.AddMovieAsync(Heat, Ct);
        await Pc.WantMovieAsync(WantHeat with { Title = "Heat", Year = 1986, TmdbId = 31939, PosterPath = null, Format = MovieFormatKind.Dvd }, Ct);
        await GoAwayAsync();

        var byTitle = await Shelf.LookupMovieAsync("heat", Ct);
        var ownedDisc = await Shelf.LookupScanAsync(new(HeatUpc), Ct);
        var wantedDisc = await Shelf.LookupScanAsync(new("043396000001"), Ct);

        byTitle.Candidates.Select(c => c.Year).ShouldBe([1995, 1986]);
        byTitle.Candidates[0].Owned.ShouldNotBeNull().Formats.ShouldBe([MovieFormatKind.BluRay]);
        ownedDisc.Movie.ShouldNotBeNull().OwnedByBarcode.ShouldNotBeNull().Title.ShouldBe("Heat");
        wantedDisc.Movie.ShouldNotBeNull().Candidates.ShouldHaveSingleItem().Wish.ShouldNotBeNull().Format.ShouldBe(MovieFormatKind.Dvd);
        _pc.Online.Tmdb.Searches.ShouldBeEmpty();
        SentToThePc().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_film_the_pc_got_meanwhile_gets_the_format_added_there()
    {
        await GoAwayAsync();
        await Pc.AddMovieAsync(Heat with { Formats = [new(MovieFormatKind.Dvd)] }, Ct);

        await Shelf.AddMovieAsync(Heat, Ct);
        await HomeAsync();

        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.BluRay, MovieFormatKind.Dvd]);
        await ThePhoneShowsWhatThePcHasAsync();
        Sync.Status.TurnedDown.ShouldBeEmpty();
    }

    [Fact]
    public async Task What_the_pc_has_already_is_dropped_without_a_word()
    {
        await Pc.WantMovieAsync(WantDune, Ct);
        await GoAwayAsync();
        var dune = (await Shelf.ListWishlistAsync(null, Ct)).Movies.ShouldHaveSingleItem();

        // Meanwhile on the PC: the same film on the same format, the same comic, and the wish taken off.
        await Pc.AddMovieAsync(Heat, Ct);
        await Pc.AddComicAsync(Saga("7") with { MetronId = 4521 }, Ct);
        await Pc.RemoveWishlistMovieAsync(dune.Id, Ct);

        await Shelf.AddMovieAsync(Heat, Ct);
        await Shelf.AddComicAsync(Saga("7") with { MetronId = 4521 }, Ct);
        await Shelf.ChangeWishlistFormatAsync(dune.Id, MovieFormatKind.BluRay, Ct);
        await Shelf.RemoveWishlistMovieAsync(dune.Id, Ct);
        Sync.Status.Waiting.ShouldBe(4);
        await HomeAsync();

        Sync.Status.TurnedDown.ShouldBeEmpty();
        (await Pc.ListMoviesAsync(new(), Ct)).Facets.FilmCount.ShouldBe(1);
        (await Pc.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldHaveSingleItem();
        await ThePhoneShowsWhatThePcHasAsync();
    }

    [Fact]
    public async Task What_the_pc_wont_take_is_told_once_and_the_rest_still_goes_through()
    {
        await GoAwayAsync();
        await Shelf.WantMovieAsync(WantDune, Ct);
        await Shelf.AddMovieAsync(Heat, Ct);
        _phone.Transport.Failing.Add("wishlist/movies");
        var told = 0;
        Sync.Changed += () => Interlocked.Increment(ref told);

        await HomeAsync();

        Sync.Status.TurnedDown.ShouldBe(["Dune (2021) on the wishlist"]);
        Sync.Status.Waiting.ShouldBe(0);
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        (await Shelf.ListWishlistAsync(null, Ct)).Movies.ShouldBeEmpty("the phone shows what the PC has");
        told.ShouldBeGreaterThan(0);

        using var reopened = new TestPhone(_pc, restartOf: _phone);
        await reopened.StartAsync();
        reopened.Sync.Status.TurnedDown.ShouldBe(["Dune (2021) on the wishlist"]);

        await reopened.Sync.DismissAsync(Ct);

        reopened.Sync.Status.TurnedDown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_away_from_home_moves_the_film_from_the_wishlist_to_the_collection_on_both()
    {
        await Pc.WantMovieAsync(WantHeat, Ct);
        await GoAwayAsync();

        await Shelf.AddMovieAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K)] }, Ct);

        (await Shelf.ListWishlistAsync(null, Ct)).MovieCount.ShouldBe(0);
        (await Pc.ListWishlistAsync(null, Ct)).MovieCount.ShouldBe(1);

        await HomeAsync();

        (await Pc.ListWishlistAsync(null, Ct)).MovieCount.ShouldBe(0);
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.Uhd4K]);
        await ThePhoneShowsWhatThePcHasAsync();
    }

    [Fact]
    public async Task Wishlist_changes_made_away_reach_the_pc_in_the_order_they_were_made()
    {
        await Pc.WantMovieAsync(WantHeat, Ct);
        await Pc.WantComicAsync(WantSaga9, Ct);
        await GoAwayAsync();
        var wishlist = await Shelf.ListWishlistAsync(null, Ct);

        await Shelf.ChangeWishlistFormatAsync(wishlist.Movies[0].Id, MovieFormatKind.BluRay, Ct);
        await Shelf.RemoveWishlistComicAsync(wishlist.Comics[0].Id, Ct);
        await Shelf.WantMovieAsync(WantDune, Ct);
        await Shelf.WantComicAsync(WantSaga9 with { Number = "10" }, Ct);

        (await Shelf.ListWishlistAsync(null, Ct)).ShouldSatisfyAllConditions(
            w => w.Movies.Select(m => (m.Title, m.Format)).ShouldBe([("Dune", MovieFormatKind.Uhd4K), ("Heat", MovieFormatKind.BluRay)], ignoreOrder: true),
            w => w.Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #10"));
        Sync.Status.Waiting.ShouldBe(4);

        await HomeAsync();

        SentToThePc().ShouldBe([$"wishlist/movies/{wishlist.Movies[0].Id}/format", $"wishlist/comics/{wishlist.Comics[0].Id}", "wishlist/movies", "wishlist/comics"]);
        (await Pc.ListWishlistAsync(null, Ct)).ShouldSatisfyAllConditions(
            w => w.Movies.Select(m => (m.Title, m.Format)).ShouldBe([("Dune", MovieFormatKind.Uhd4K), ("Heat", MovieFormatKind.BluRay)], ignoreOrder: true),
            w => w.Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #10"));
        await ThePhoneShowsWhatThePcHasAsync();
    }

    [Fact]
    public async Task A_wish_made_and_taken_back_before_it_was_sent_never_reaches_the_pc()
    {
        await GoAwayAsync();
        var dune = (await Shelf.WantMovieAsync(WantDune, Ct)).Movie.ShouldNotBeNull();
        var saga = (await Shelf.WantComicAsync(WantSaga9, Ct)).Comic.ShouldNotBeNull();
        Sync.IsUnsent(dune.Id).ShouldBeTrue();

        (await Shelf.RemoveWishlistMovieAsync(dune.Id, Ct)).ShouldBeTrue();
        (await Shelf.RemoveWishlistComicAsync(saga.Id, Ct)).ShouldBeTrue();

        Sync.Status.Waiting.ShouldBe(0);
        await HomeAsync();
        SentToThePc().ShouldBeEmpty();
        (await Pc.ListWishlistAsync(null, Ct)).ShouldSatisfyAllConditions(w => w.MovieCount.ShouldBe(0), w => w.ComicCount.ShouldBe(0));
    }

    [Fact]
    public async Task A_wish_changed_before_it_was_sent_is_asked_of_the_pc_once_as_it_ended_up()
    {
        await GoAwayAsync();
        var dune = (await Shelf.WantMovieAsync(WantDune, Ct)).Movie.ShouldNotBeNull();

        await Shelf.ChangeWishlistFormatAsync(dune.Id, MovieFormatKind.BluRay, Ct);
        await Shelf.WantMovieAsync(WantDune with { Format = MovieFormatKind.Dvd }, Ct);

        Sync.Status.Waiting.ShouldBe(1);
        await HomeAsync();
        SentToThePc().ShouldBe(["wishlist/movies"]);
        (await Pc.ListWishlistAsync(null, Ct)).Movies.ShouldHaveSingleItem().Format.ShouldBe(MovieFormatKind.Dvd);
        await ThePhoneShowsWhatThePcHasAsync();
    }

    [Fact]
    public async Task Wanting_a_comic_already_on_the_wishlist_is_nothing_to_tell_the_pc()
    {
        await Pc.WantComicAsync(WantSaga9, Ct);
        await GoAwayAsync();

        (await Shelf.WantComicAsync(WantSaga9, Ct)).Status.ShouldBe(SaveStatus.Saved);

        Sync.Status.Waiting.ShouldBe(0);
    }

    [Fact]
    public async Task Missing_issues_wanted_away_are_each_asked_for_and_one_taken_back_isnt()
    {
        _pc.Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6"]));
        await Pc.AddComicAsync(Saga("1"), Ct);
        await Pc.AddComicAsync(Saga("6"), Ct);
        await GoAwayAsync();

        var first = await Shelf.WantMissingIssuesAsync(new(1234, "2", "5"), Ct);

        first.Comic.ShouldNotBeNull().DisplayName.ShouldBe("Saga #2");
        var wanted = (await Shelf.ListWishlistAsync(null, Ct)).Comics;
        wanted.Select(c => c.Number).ShouldBe(["2", "3", "4", "5"], ignoreOrder: true);
        Sync.Status.Waiting.ShouldBe(4);

        await Shelf.RemoveWishlistComicAsync(wanted.Single(c => c.Number == "4").Id, Ct);
        await HomeAsync();

        SentToThePc().ShouldBe(["wishlist/comics/missing", "wishlist/comics/missing", "wishlist/comics/missing"]);
        (await Pc.ListWishlistAsync(null, Ct)).Comics.Select(c => c.Number).ShouldBe(["2", "3", "5"], ignoreOrder: true);
        (await Pc.ListWishlistAsync(null, Ct)).Comics.ShouldAllBe(c => c.FromMissing);
        await ThePhoneShowsWhatThePcHasAsync();
    }

    [Fact]
    public async Task Something_added_on_the_phone_can_be_removed_there_until_it_is_sent()
    {
        await GoAwayAsync();
        var heat = (await Shelf.AddMovieAsync(Heat, Ct)).Movie.ShouldNotBeNull();
        await Shelf.AddMovieAsync(Heat with { Formats = [new(MovieFormatKind.Dvd)] }, Ct);
        var saga = (await Shelf.AddComicAsync(Saga("7"), Ct)).Comic.ShouldNotBeNull();
        Sync.Status.Waiting.ShouldBe(3);
        var told = 0;
        Shelf.Changed += () => Interlocked.Increment(ref told);

        (await Shelf.DeleteMovieAsync(heat.Id, Ct)).ShouldBeTrue();
        (await Shelf.DeleteComicAsync(saga.Id, Ct)).ShouldBeTrue();

        told.ShouldBeGreaterThanOrEqualTo(2);
        Sync.Status.Waiting.ShouldBe(0);
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
        (await Shelf.GetComicAsync(saga.Id, Ct)).ShouldBeNull();
        await HomeAsync();
        SentToThePc().ShouldBeEmpty();
        (await Pc.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task What_the_pc_has_is_edited_and_removed_on_the_pc_only()
    {
        var heat = (await Pc.AddMovieAsync(Heat, Ct)).Movie.ShouldNotBeNull();
        var saga = (await Pc.AddComicAsync(Saga("7"), Ct)).Comic.ShouldNotBeNull();
        await GoAwayAsync();

        // A format added on the phone to a film the PC has doesn't make the film the phone's to remove.
        await Shelf.AddMovieAsync(Heat with { Formats = [new(MovieFormatKind.Dvd)] }, Ct);

        Sync.IsUnsent(heat.Id).ShouldBeFalse();
        await Should.ThrowAsync<NotSupportedException>(() => Shelf.DeleteMovieAsync(heat.Id, Ct));
        await Should.ThrowAsync<NotSupportedException>(() => Shelf.DeleteComicAsync(saga.Id, Ct));
        await Should.ThrowAsync<NotSupportedException>(() => Shelf.UpdateMovieAsync(heat.Id, heat.Version, Heat, Ct));
        await Should.ThrowAsync<NotSupportedException>(() => Shelf.UpdateComicAsync(saga.Id, saga.Version, Saga("7"), Ct));
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.BluRay, MovieFormatKind.Dvd]);
    }

    [Fact]
    public async Task A_save_that_doesnt_go_through_on_the_phone_isnt_sent_anywhere()
    {
        await Pc.AddMovieAsync(Heat, Ct);
        await GoAwayAsync();

        var invalid = await Shelf.AddMovieAsync(Heat with { Title = "" }, Ct);
        var owned = await Shelf.AddMovieAsync(Heat, Ct);
        var gone = await Shelf.RemoveWishlistMovieAsync(Guid.NewGuid(), Ct);

        invalid.Status.ShouldBe(SaveStatus.Invalid);
        owned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        gone.ShouldBeFalse();
        Sync.Status.Waiting.ShouldBe(0);
    }

    [Fact]
    public async Task Screens_are_told_as_changes_wait_and_as_they_go()
    {
        await GoAwayAsync();
        var waiting = new List<int>();
        Sync.Changed += () =>
        {
            lock (waiting)
            {
                waiting.Add(Sync.Status.Waiting);
            }
        };

        await Shelf.WantMovieAsync(WantDune, Ct);
        await Shelf.AddMovieAsync(Heat, Ct);
        await HomeAsync();

        lock (waiting)
        {
            waiting.ShouldContain(1);
            waiting.ShouldContain(2);
            waiting[^1].ShouldBe(0);
        }
    }
}
