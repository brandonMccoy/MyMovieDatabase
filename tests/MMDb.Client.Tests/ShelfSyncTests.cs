using Microsoft.Data.Sqlite;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Phone;
using MMDb.Contracts.Wishlist;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>The phone's own copy of the PC's collection: what a sync brings over, and what it leaves alone.</summary>
public sealed class ShelfSyncTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ICollectionApi Pc => _pc.Api;

    /// <summary>What the phone's screens read.</summary>
    private ICollectionApi Shelf => _phone.Shelf;

    private ShelfSync Sync => _phone.Sync;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Lt. Vincent Hanna"), new("Robert De Niro", "Neil McCauley")],
        Crew = [new("Dante Spinotti", "Director of Photography", "Camera")],
        Genres = ["Crime", "Drama"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "Obsessive master thief Neil McCauley leads a top-notch crew.",
        TmdbId = 949,
        PosterPath = "/heat.jpg",
        Formats = [new(MovieFormatKind.Uhd4K, Upc: "786936894424"), new(MovieFormatKind.Dvd)],
    };

    private static SaveMovieRequest Alien => new() { Title = "Alien", Year = 1979, Directors = ["Ridley Scott"], Genres = ["Horror"], Formats = [new(MovieFormatKind.Dvd)] };

    private static SaveComicRequest Saga(string number) => new() { Series = "Saga", SeriesYear = 2012, Number = number, Publisher = "Image", MetronSeriesId = 1234 };

    private static WantMovieRequest WantDune => new() { Title = "Dune", Year = 2021, Directors = ["Denis Villeneuve"], TmdbId = 438631, PosterPath = "/dune.jpg", Format = MovieFormatKind.Uhd4K };

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

    /// <summary>A sync, and the covers that follow it in the background.</summary>
    private async Task SyncedAsync()
    {
        await Sync.SyncAsync(Ct);
        await Sync.Covers.WaitAsync(Ct);
    }

    private async Task<IReadOnlyList<string>> AskedDuringAsync(Func<Task> act)
    {
        // Covers from a sync before are still arriving in the background.
        await Sync.Covers.WaitAsync(Ct);
        var before = _phone.Transport.Asked.Count;
        await act();
        return [.. _phone.Transport.Asked.Skip(before).Select(a => a[(a.IndexOf("/api/v1/", StringComparison.Ordinal) + "/api/v1/".Length)..])];
    }

    private async Task ThePhoneShowsWhatThePcHasAsync()
    {
        Wire.Json(await Shelf.ListMoviesAsync(new(), Ct)).ShouldBe(Wire.Json(await Pc.ListMoviesAsync(new(), Ct)));
        Wire.Json(await Shelf.ListComicsAsync(new(), Ct)).ShouldBe(Wire.Json(await Pc.ListComicsAsync(new(), Ct)));
        Wire.Json(await Shelf.ListWishlistAsync(null, Ct)).ShouldBe(Wire.Json(await Pc.ListWishlistAsync(null, Ct)));
    }

    [Fact]
    public async Task A_phone_that_never_synced_has_nothing_and_says_so()
    {
        Wire.Json(Sync.Status).ShouldBe(Wire.Json(new PhoneSyncStatus(PhoneSyncState.Idle, SyncedAt: null, Waiting: 0, TurnedDown: [])));
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_sync_copies_the_collection_and_wishlist_and_the_phone_then_shows_them_without_the_pc()
    {
        _pc.Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));
        var heat = (await Pc.AddMovieAsync(Heat, Ct)).Movie.ShouldNotBeNull();
        await Pc.AddMovieAsync(Alien, Ct);
        var saga = (await Pc.AddComicAsync(Saga("2"), Ct)).Comic.ShouldNotBeNull();
        await Pc.AddComicAsync(Saga("7"), Ct);
        var dune = (await Pc.WantMovieAsync(WantDune, Ct)).Movie.ShouldNotBeNull();
        await Pc.WantMissingIssuesAsync(new(1234, "3", "4"), Ct);
        _pc.Clock.Advance(TimeSpan.FromHours(2));

        await SyncedAsync();
        _phone.Away = true;

        await ThePhoneShowsWhatThePcHasAsync();
        Wire.Json(await Shelf.GetMovieAsync(heat.Id, Ct)).ShouldBe(Wire.Json(await Pc.GetMovieAsync(heat.Id, Ct)));
        Wire.Json(await Shelf.GetComicAsync(saga.Id, Ct)).ShouldBe(Wire.Json(await Pc.GetComicAsync(saga.Id, Ct)), "with the run's missing issues, from the series the PC knows");
        Wire.Json(await Shelf.GetWishlistMovieAsync(dune.Id, Ct)).ShouldBe(Wire.Json(await Pc.GetWishlistMovieAsync(dune.Id, Ct)));
        (await Shelf.ListMoviesAsync(new() { Search = "pacino" }, Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        Wire.Json(Sync.Status).ShouldBe(Wire.Json(new PhoneSyncStatus(PhoneSyncState.Idle, SyncedAt: TestPc.Start.AddHours(2), Waiting: 0, TurnedDown: [])));
    }

    [Fact]
    public async Task A_later_sync_fetches_only_what_changed_on_the_pc_and_drops_what_went()
    {
        var heat = (await Pc.AddMovieAsync(Heat, Ct)).Movie.ShouldNotBeNull();
        var alien = (await Pc.AddMovieAsync(Alien, Ct)).Movie.ShouldNotBeNull();
        var dune = (await Pc.WantMovieAsync(WantDune, Ct)).Movie.ShouldNotBeNull();
        await SyncedAsync();

        _pc.Clock.Advance(TimeSpan.FromDays(1));
        await Pc.UpdateMovieAsync(heat.Id, heat.Version, Heat with { Title = "Heat (Director's Definitive Edition)", Formats = [new(MovieFormatKind.Uhd4K)] }, Ct);
        await Pc.DeleteMovieAsync(alien.Id, Ct);
        await Pc.RemoveWishlistMovieAsync(dune.Id, Ct);
        await Pc.AddComicAsync(Saga("7"), Ct);

        var asked = await AskedDuringAsync(() => Sync.SyncAsync(Ct));

        asked.ShouldBe(["keys", "sync", "sync/rows", "sync/rows"], "one request for the film that changed, one for the new comic");
        await ThePhoneShowsWhatThePcHasAsync();
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.Uhd4K]);
        (await Shelf.GetMovieAsync(alien.Id, Ct)).ShouldBeNull();
        Sync.Status.SyncedAt.ShouldBe(TestPc.Start.AddDays(1));
    }

    [Fact]
    public async Task With_nothing_new_on_the_pc_a_sync_asks_for_no_rows()
    {
        await Pc.AddMovieAsync(Heat, Ct);
        await SyncedAsync();

        var asked = await AskedDuringAsync(() => Sync.SyncAsync(Ct));

        asked.ShouldBe(["keys", "sync"]);
    }

    [Fact]
    public async Task A_big_collection_is_fetched_a_hundred_rows_at_a_time()
    {
        for (var i = 0; i < 230; i++)
        {
            await Pc.AddMovieAsync(new() { Title = $"Film {i}", Formats = [new(MovieFormatKind.Dvd)] }, Ct);
        }

        var asked = await AskedDuringAsync(() => Sync.SyncAsync(Ct));

        asked.Count(a => a == "sync/rows").ShouldBe(3);
        (await Shelf.ListMoviesAsync(new(), Ct)).Facets.FilmCount.ShouldBe(230);
    }

    [Fact]
    public async Task Away_from_the_pc_a_sync_changes_nothing_and_the_copy_stays_readable()
    {
        await Pc.AddMovieAsync(Heat, Ct);
        await SyncedAsync();
        _pc.Clock.Advance(TimeSpan.FromDays(3));
        await Pc.AddMovieAsync(Alien, Ct);
        _phone.Away = true;
        var told = 0;
        Sync.Changed += () => Interlocked.Increment(ref told);

        await SyncedAsync();

        _phone.Link.State.ShouldBe(PcLinkState.Unreachable);
        Wire.Json(Sync.Status).ShouldBe(Wire.Json(new PhoneSyncStatus(PhoneSyncState.Idle, SyncedAt: TestPc.Start, Waiting: 0, TurnedDown: [])));
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        told.ShouldBeGreaterThan(0);

        _phone.Away = false;
        await SyncedAsync();

        (await Shelf.ListMoviesAsync(new(), Ct)).Facets.FilmCount.ShouldBe(2);
    }

    [Fact]
    public async Task Open_screens_are_told_when_a_sync_brought_something_and_not_when_it_didnt()
    {
        await Pc.AddMovieAsync(Heat, Ct);
        var told = 0;
        Shelf.Changed += () => Interlocked.Increment(ref told);

        await SyncedAsync();
        var afterFirst = Volatile.Read(ref told);
        await SyncedAsync();

        afterFirst.ShouldBeGreaterThan(0);
        Volatile.Read(ref told).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task A_phone_and_pc_on_different_versions_of_shelf_dont_sync()
    {
        await Pc.AddMovieAsync(Heat, Ct);

        // As if the phone's app were newer than the PC's: its database has a migration the PC's hasn't.
        await using (var db = new SqliteConnection(_phone.ConnectionString))
        {
            await db.OpenAsync(Ct);
            await using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99990101000000_Later', '10.0.0')";
            await command.ExecuteNonQueryAsync(Ct);
        }

        var asked = await AskedDuringAsync(() => Sync.SyncAsync(Ct));

        asked.ShouldBe(["keys", "sync"]);
        Sync.Status.State.ShouldBe(PhoneSyncState.WrongVersion);
        Sync.Status.SyncedAt.ShouldBeNull();
        (await Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_phone_that_isnt_paired_has_nothing_to_sync_with()
    {
        await _phone.Link.ForgetAsync(Ct);

        var asked = await AskedDuringAsync(() => Sync.SyncAsync(Ct));

        asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_a_sync_the_phone_keeps_a_smaller_copy_of_every_cover_for_when_the_pc_is_away()
    {
        var heat = (await Pc.AddMovieAsync(Heat, Ct)).Movie.ShouldNotBeNull();
        var dune = (await Pc.WantMovieAsync(WantDune, Ct)).Movie.ShouldNotBeNull();
        var heatCover = Uri.UnescapeDataString(heat.CoverUrl.ShouldNotBeNull().Segments[^1]);
        var duneCover = Uri.UnescapeDataString(dune.CoverUrl.ShouldNotBeNull().Segments[^1]);

        var asked = await AskedDuringAsync(async () =>
        {
            await SyncedAsync();
            await Sync.Covers.WaitAsync(Ct);
        });
        _phone.Away = true;

        asked.Count(a => a.StartsWith("covers/", StringComparison.Ordinal)).ShouldBe(2);
        Directory.GetFiles(_phone.Files["covers"]).Select(Path.GetFileName).ShouldBe([heatCover, duneCover], ignoreOrder: true);
        (await _phone.Covers.GetAsync(heatCover, cancellationToken: Ct)).ShouldNotBeNull().Bytes.ShouldBe(FakePosters.ImageFor("/heat.jpg"));
    }

    [Fact]
    public async Task Covers_of_what_the_pc_no_longer_has_are_cleared_out_once_they_have_sat_a_while()
    {
        var alien = (await Pc.AddMovieAsync(Alien with { TmdbId = 348, PosterPath = "/alien.jpg" }, Ct)).Movie.ShouldNotBeNull();
        await Pc.AddMovieAsync(Heat, Ct);
        await SyncedAsync();
        await Sync.Covers.WaitAsync(Ct);
        var alienCover = Path.Combine(_phone.Files["covers"], Uri.UnescapeDataString(alien.CoverUrl.ShouldNotBeNull().Segments[^1]));
        File.Exists(alienCover).ShouldBeTrue();

        await Pc.DeleteMovieAsync(alien.Id, Ct);
        await SyncedAsync();
        await Sync.Covers.WaitAsync(Ct);
        File.Exists(alienCover).ShouldBeTrue("it may belong to something being saved this minute");

        File.SetLastWriteTimeUtc(alienCover, DateTime.UtcNow.AddHours(-2));
        await Pc.AddMovieAsync(Alien, Ct);
        await SyncedAsync();
        await Sync.Covers.WaitAsync(Ct);

        File.Exists(alienCover).ShouldBeFalse();
        Directory.GetFiles(_phone.Files["covers"]).ShouldHaveSingleItem();
    }
}
