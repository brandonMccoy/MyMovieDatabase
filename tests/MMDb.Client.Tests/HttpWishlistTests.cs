using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>The phone's view of the wishlist is the PC's own, call for call, over the web API.</summary>
public sealed class HttpWishlistTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ICollectionApi Phone => _phone.Api;

    private ICollectionApi Pc => _pc.Api;

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

    private static WantMovieRequest Alien => new() { Title = "Alien", Year = 1979, Directors = ["Ridley Scott"], Format = MovieFormatKind.BluRay };

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

    [Theory]
    [InlineData(null)]
    [InlineData("scott")]
    [InlineData("Mann & co")]
    [InlineData("nothing like this")]
    public async Task The_phone_lists_the_wishlist_exactly_as_the_pc_does(string? search)
    {
        await Pc.AddMovieAsync(new() { Title = "Heat", TmdbId = 949, Formats = [new(MovieFormatKind.Dvd)] }, Ct);
        await Pc.WantMovieAsync(Heat, Ct);
        _pc.Clock.Advance(TimeSpan.FromMinutes(1));
        await Pc.WantMovieAsync(Alien, Ct);

        var onPhone = await Phone.ListWishlistAsync(search, Ct);

        Wire.Json(onPhone).ShouldBe(Wire.Json(await Pc.ListWishlistAsync(search, Ct)));
        onPhone.MovieCount.ShouldBe(2);
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

    private async Task OwnSagaAsync(params string[] numbers)
    {
        _pc.Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));
        foreach (var number in numbers)
        {
            await Pc.AddComicAsync(new() { Series = "Saga", SeriesYear = 2012, Number = number, MetronSeriesId = 1234 }, Ct);
        }
    }

    [Fact]
    public async Task Comics_wanted_on_the_phone_are_on_the_pcs_wishlist_and_listed_the_same()
    {
        await OwnSagaAsync("1", "6");

        var issue = await Phone.WantComicAsync(Saga7, Ct);
        var run = await Phone.WantMissingIssuesAsync(new(1234, "2", "5"), Ct);
        var owned = await Phone.WantComicAsync(Saga7 with { Number = "6", MetronId = null }, Ct);
        var unknown = await Phone.WantMissingIssuesAsync(new(999, "1"), Ct);
        var invalid = await Phone.WantComicAsync(Saga7 with { Series = null }, Ct);

        issue.Status.ShouldBe(SaveStatus.Saved);
        run.Comic!.DisplayName.ShouldBe("Saga #2");
        owned.Status.ShouldBe(SaveStatus.Saved, "only an issue found on Metron is known to be owned");
        unknown.Status.ShouldBe(SaveStatus.NotFound);
        invalid.Status.ShouldBe(SaveStatus.Invalid);
        invalid.Errors.ShouldHaveSingleItem().Field.ShouldBe("Series");
        var onPhone = await Phone.ListWishlistAsync(cancellationToken: Ct);
        Wire.Json(onPhone).ShouldBe(Wire.Json(await Pc.ListWishlistAsync(cancellationToken: Ct)));
        onPhone.ComicCount.ShouldBe(6);
        Wire.Json(await Phone.ListComicsAsync(new(), Ct)).ShouldBe(Wire.Json(await Pc.ListComicsAsync(new(), Ct)));
    }

    [Fact]
    public async Task The_phone_can_buy_or_drop_a_wanted_comic()
    {
        _pc.Online.Metron.Add(TestComics.Saga7);
        var item = (await Pc.WantComicAsync(Saga7, Ct)).Comic!;
        var changes = 0;
        Phone.Changed += () => changes++;

        var found = (await Phone.LookupWishlistComicAsync(item.Id, Ct)).ShouldNotBeNull();

        Wire.Json(found).ShouldBe(Wire.Json(await Pc.LookupWishlistComicAsync(item.Id, Ct)));
        found.Wish.ShouldBe(new ComicWishDto(item.Id, "Saga #7", "Saga", 2012, "7"));
        found.Candidates.ShouldHaveSingleItem().Wish.ShouldBe(found.Wish);
        (await Phone.RemoveWishlistComicAsync(item.Id, Ct)).ShouldBeTrue();
        (await Phone.RemoveWishlistComicAsync(item.Id, Ct)).ShouldBeFalse();
        (await Phone.LookupWishlistComicAsync(item.Id, Ct)).ShouldBeNull();
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task A_film_wanted_on_the_phone_is_on_the_pcs_wishlist()
    {
        var response = await Phone.WantMovieAsync(Heat, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var onPc = (await Pc.ListWishlistAsync(cancellationToken: Ct)).Movies.ShouldHaveSingleItem();
        Wire.Json(response.Movie).ShouldBe(Wire.Json(onPc));
        onPc.Format.ShouldBe(MovieFormatKind.Uhd4K);
    }

    [Fact]
    public async Task The_phone_opens_one_wishlist_item_and_changes_a_films_wanted_format()
    {
        await Pc.AddMovieAsync(new() { Title = "Heat", TmdbId = 949, Formats = [new(MovieFormatKind.Dvd)] }, Ct);
        var film = (await Pc.WantMovieAsync(Heat, Ct)).Movie!;
        var comic = (await Pc.WantComicAsync(Saga7, Ct)).Comic!;
        var changes = 0;
        Phone.Changed += () => changes++;

        Wire.Json(await Phone.GetWishlistMovieAsync(film.Id, Ct)).ShouldBe(Wire.Json(film));
        Wire.Json(await Phone.GetWishlistComicAsync(comic.Id, Ct)).ShouldBe(Wire.Json(comic));
        (await Phone.GetWishlistMovieAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
        (await Phone.GetWishlistComicAsync(Guid.NewGuid(), Ct)).ShouldBeNull();

        var changed = await Phone.ChangeWishlistFormatAsync(film.Id, MovieFormatKind.BluRay, Ct);
        var owned = await Phone.ChangeWishlistFormatAsync(film.Id, MovieFormatKind.Dvd, Ct);
        var gone = await Phone.ChangeWishlistFormatAsync(Guid.NewGuid(), MovieFormatKind.BluRay, Ct);

        changed.Status.ShouldBe(SaveStatus.Saved);
        Wire.Json(changed.Movie).ShouldBe(Wire.Json(await Pc.GetWishlistMovieAsync(film.Id, Ct)));
        changed.Movie!.Format.ShouldBe(MovieFormatKind.BluRay);
        owned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        gone.Status.ShouldBe(SaveStatus.NotFound);
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task What_the_pc_refuses_to_put_on_the_wishlist_comes_back_with_the_reason()
    {
        var owned = (await Pc.AddMovieAsync(new() { Title = "Heat", TmdbId = 949, Formats = [new(MovieFormatKind.Uhd4K)] }, Ct)).Movie!;

        var alreadyOwned = await Phone.WantMovieAsync(Heat, Ct);
        var invalid = await Phone.WantMovieAsync(Alien with { Title = " " }, Ct);

        alreadyOwned.Status.ShouldBe(SaveStatus.AlreadyOwned);
        alreadyOwned.Owned!.Id.ShouldBe(owned.Id);
        invalid.Status.ShouldBe(SaveStatus.Invalid);
        invalid.Errors.ShouldHaveSingleItem().Field.ShouldBe("Title");
    }

    [Fact]
    public async Task The_phone_can_take_a_film_off_the_wishlist()
    {
        var item = (await Pc.WantMovieAsync(Heat, Ct)).Movie!;

        (await Phone.RemoveWishlistMovieAsync(item.Id, Ct)).ShouldBeTrue();
        (await Phone.RemoveWishlistMovieAsync(item.Id, Ct)).ShouldBeFalse();
        (await Pc.ListWishlistAsync(cancellationToken: Ct)).MovieCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_phone_announces_what_it_put_on_or_took_off_the_wishlist_and_not_what_was_refused()
    {
        var changes = 0;
        Phone.Changed += () => changes++;

        var item = (await Phone.WantMovieAsync(Heat, Ct)).Movie!;
        await Phone.WantMovieAsync(Alien with { Title = " " }, Ct);
        await Phone.ListWishlistAsync(cancellationToken: Ct);
        await Phone.RemoveWishlistMovieAsync(Guid.NewGuid(), Ct);
        changes.ShouldBe(1);

        await Phone.RemoveWishlistMovieAsync(item.Id, Ct);
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task Got_it_on_the_phone_is_looked_up_by_the_pc()
    {
        _pc.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var item = (await Pc.WantMovieAsync(Heat, Ct)).Movie!;

        var found = (await Phone.LookupWishlistMovieAsync(item.Id, Ct)).ShouldNotBeNull();

        Wire.Json(found).ShouldBe(Wire.Json(await Pc.LookupWishlistMovieAsync(item.Id, Ct)));
        found.Wish.ShouldBe(new MovieWishDto(item.Id, MovieFormatKind.Uhd4K, "Heat", 1995));
        found.Candidates.ShouldHaveSingleItem().Wish.ShouldBe(found.Wish);
        (await Phone.LookupWishlistMovieAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }
}
