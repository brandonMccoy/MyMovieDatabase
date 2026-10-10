using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;
using MMDb.Testing;

namespace MMDb.Application.Tests;

public sealed class WishlistTests
{
    private static MovieDetails Dune => new() { Title = "Dune", Year = 2021, Directors = ["Denis Villeneuve"], TmdbId = 438631, PosterPath = "/dune.jpg" };

    private static MovieDetails Amelie => new() { Title = "Amélie", Year = 2001, Directors = ["Jean-Pierre Jeunet"], TmdbId = 194 };

    [Fact]
    public async Task A_wanted_film_is_listed_with_the_format_wanted_and_its_poster_is_kept()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var wishlist = await shelf.WishlistAsync();
        wishlist.MovieCount.ShouldBe(1);
        wishlist.Movies.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            f => f.Item.Id.ShouldBe(item.Id),
            f => f.Item.Title.ShouldBe("Heat"),
            f => f.Item.Year.ShouldBe(1995),
            f => f.Item.Directors.ShouldBe(["Michael Mann"]),
            f => f.Item.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ"),
            f => f.Item.Format.ShouldBe(MediaFormat.Uhd4K),
            f => f.Item.CreatedAt.ShouldBe(TestShelf.Start),
            f => f.Owned.ShouldBeEmpty());
        shelf.Online.Covers.Files[item.CoverImage.ShouldNotBeNull()].ShouldBe(FakePosters.ImageFor("/heat.jpg"));
    }

    [Fact]
    public async Task The_wishlist_is_not_the_collection()
    {
        await using var shelf = await TestShelf.CreateAsync();

        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        (await shelf.ListAsync()).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_film_wanted_last_is_listed_first()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        shelf.Clock.Advance(TimeSpan.FromDays(1));
        await shelf.WantAsync(Dune, MediaFormat.BluRay);

        (await shelf.WishlistAsync()).Movies.Select(f => f.Item.Title).ShouldBe(["Dune", "Heat"]);
    }

    [Theory]
    [InlineData("heat", "Heat")]
    [InlineData("amelie", "Amélie")]
    [InlineData("villeneuve", "Dune")]
    [InlineData("2021", "Dune")]
    [InlineData("michael heat", "Heat")]
    public async Task Search_finds_a_wish_by_title_director_or_year(string search, string expected)
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        await shelf.WantAsync(Dune, MediaFormat.Uhd4K);
        await shelf.WantAsync(Amelie, MediaFormat.BluRay);

        var wishlist = await shelf.WishlistAsync(search);

        wishlist.Movies.ShouldHaveSingleItem().Item.Title.ShouldBe(expected);
        wishlist.MovieCount.ShouldBe(3, "the count is of the whole wishlist");
    }

    [Fact]
    public async Task Wanting_a_film_again_changes_the_format_and_keeps_it_one_item()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var first = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        shelf.Clock.Advance(TimeSpan.FromDays(3));

        var again = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.BluRay);

        again.Id.ShouldBe(first.Id);
        var item = (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem().Item;
        item.Format.ShouldBe(MediaFormat.BluRay);
        item.CreatedAt.ShouldBe(TestShelf.Start);
        shelf.Online.Covers.Files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_wanted_film_is_fetched_with_what_is_owned_of_it()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var film = (await shelf.Wishlist((wishlist, ct) => wishlist.GetMovieAsync(item.Id, ct))).ShouldNotBeNull();

        film.Item.Title.ShouldBe("Heat");
        film.Owned.ShouldBe([MediaFormat.Dvd]);
        (await shelf.Wishlist((wishlist, ct) => wishlist.GetMovieAsync(Guid.NewGuid(), ct))).ShouldBeNull();
    }

    [Fact]
    public async Task The_wanted_format_can_be_changed_and_everything_else_stays()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        shelf.Clock.Advance(TimeSpan.FromDays(2));

        var result = await shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(item.Id, MediaFormat.BluRay, ct));

        result.ShouldBeOfType<WantMovieResult.Saved>().Film.Item.Format.ShouldBe(MediaFormat.BluRay);
        var saved = (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem().Item;
        saved.ShouldSatisfyAllConditions(
            s => s.Id.ShouldBe(item.Id),
            s => s.Format.ShouldBe(MediaFormat.BluRay),
            s => s.CoverImage.ShouldBe(item.CoverImage),
            s => s.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ"),
            s => s.CreatedAt.ShouldBe(TestShelf.Start));
        shelf.Online.Posters.Fetched.Count.ShouldBe(1, "the poster isn't fetched again");
    }

    [Fact]
    public async Task The_wanted_format_cannot_be_changed_to_one_the_film_is_owned_on_or_that_isnt_one()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var owned = await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var toOwned = await shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(item.Id, MediaFormat.Dvd, ct));
        var toNothing = await shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(item.Id, (MediaFormat)(-1), ct));
        var notWanted = await shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(Guid.NewGuid(), MediaFormat.BluRay, ct));

        toOwned.ShouldBeOfType<WantMovieResult.AlreadyOwned>().Movie.Id.ShouldBe(owned.Id);
        toNothing.ShouldBeOfType<WantMovieResult.Invalid>().Errors.ShouldHaveSingleItem().Field.ShouldBe(WishlistFields.Format);
        notWanted.ShouldBeOfType<WantMovieResult.NotFound>();
        (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem().Item.Format.ShouldBe(MediaFormat.Uhd4K);
    }

    [Fact]
    public async Task A_film_owned_on_that_format_is_not_wanted()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var owned = await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var result = await shelf.Wishlist((wishlist, ct) => wishlist.WantMovieAsync(TestMovies.Wanted(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K), ct));

        result.ShouldBeOfType<WantMovieResult.AlreadyOwned>().Movie.Id.ShouldBe(owned.Id);
        (await shelf.WishlistAsync()).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_film_owned_on_another_format_is_an_upgrade()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd, MediaFormat.Digital);

        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem().Owned.ShouldBe([MediaFormat.Dvd, MediaFormat.Digital]);
    }

    [Fact]
    public async Task An_invalid_wish_is_refused_with_its_errors()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var result = await shelf.Wishlist((wishlist, ct) =>
            wishlist.WantMovieAsync(TestMovies.Wanted(TestMovies.Titled(" "), (MediaFormat)(-1)), ct));

        result.ShouldBeOfType<WantMovieResult.Invalid>().Errors.Select(e => e.Field).ShouldBe([MovieFields.Title, WishlistFields.Format], ignoreOrder: true);
        (await shelf.WishlistAsync()).MovieCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_film_no_longer_wanted_is_gone_with_its_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var removed = await shelf.Wishlist((wishlist, ct) => wishlist.RemoveMovieAsync(item.Id, ct));

        removed.ShouldBeTrue();
        (await shelf.WishlistAsync()).MovieCount.ShouldBe(0);
        shelf.Online.Covers.Files.ShouldBeEmpty();
        (await shelf.Wishlist((wishlist, ct) => wishlist.RemoveMovieAsync(item.Id, ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Adding_the_film_on_the_wanted_format_takes_it_off_the_wishlist()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var movie = await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        (await shelf.WishlistAsync()).MovieCount.ShouldBe(0);
        // The wish's cover is gone, the film's is kept.
        shelf.Online.Covers.Files.Keys.ShouldHaveSingleItem().ShouldBe(movie.CoverImage);
    }

    [Fact]
    public async Task Adding_it_on_another_format_leaves_it_wanted()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        var wanted = (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem();
        wanted.Item.Format.ShouldBe(MediaFormat.Uhd4K);
        wanted.Owned.ShouldBe([MediaFormat.Dvd]);
    }

    [Fact]
    public async Task Buying_the_upgrade_of_a_film_already_owned_takes_it_off_the_wishlist()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        (await shelf.WishlistAsync()).MovieCount.ShouldBe(0);
    }

    [Fact]
    public async Task Switching_the_wanted_format_on_while_editing_takes_it_off_the_wishlist()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var movie = await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.BluRay);

        var result = await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(movie.Id, movie.Version, TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd, MediaFormat.BluRay), ct));

        result.ShouldBeOfType<SaveMovieResult.Saved>();
        (await shelf.WishlistAsync()).MovieCount.ShouldBe(0);
    }

    [Fact]
    public async Task Another_film_being_added_leaves_the_wishlist_alone()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        await shelf.AddAsync(TestMovies.Heat1986OnTmdb, MediaFormat.Uhd4K);

        (await shelf.WishlistAsync()).MovieCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_wish_entered_without_tmdb_is_granted_by_the_film_of_that_title_and_year()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.WantAsync(TestMovies.Titled("Heat", 1995), MediaFormat.Dvd);
        await shelf.WantAsync(TestMovies.Titled("Heat", 1986), MediaFormat.Dvd);

        await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        (await shelf.WishlistAsync()).Movies.ShouldHaveSingleItem().Item.Year.ShouldBe(1986);
    }

    [Fact]
    public async Task A_lookup_says_which_matches_are_on_the_wishlist()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        var item = await shelf.WantAsync(TestMovies.Heat1986OnTmdb, MediaFormat.BluRay);

        var result = await shelf.LookupAsync("heat");

        result.Matches.Select(m => m.Wanted?.Id).ShouldBe([null, item.Id]);
        result.Matches[1].Wanted!.Format.ShouldBe(MediaFormat.BluRay);
        result.Wish.ShouldBeNull();
    }

    [Fact]
    public async Task A_scanned_barcode_says_the_film_is_on_the_wishlist()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        shelf.Online.Barcodes.Add("786936894424", "Heat [4K UHD]");
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var scan = await shelf.ScanAsync("786936894424");

        scan.Movie!.Matches.ShouldHaveSingleItem().Wanted!.Id.ShouldBe(item.Id);
    }

    [Fact]
    public async Task Got_it_looks_the_film_up_by_its_tmdb_entry_without_searching()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        var result = (await shelf.LookupWishAsync(item.Id)).ShouldNotBeNull();

        result.Outcome.ShouldBe(LookupOutcome.Found);
        result.Wish!.Id.ShouldBe(item.Id);
        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Cast.ShouldBe(TestMovies.Heat.Cast, "the review screen gets the film in full");
        match.Wanted!.Id.ShouldBe(item.Id);
        shelf.Online.Tmdb.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_for_a_wish_entered_without_tmdb_searches_for_its_title_and_year()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        var item = await shelf.WantAsync(TestMovies.Titled("Heat", 1995), MediaFormat.Dvd);

        var result = (await shelf.LookupWishAsync(item.Id)).ShouldNotBeNull();

        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.TmdbId.ShouldBe(949);
        match.Wanted!.Id.ShouldBe(item.Id);
        shelf.Online.Tmdb.Searches.ShouldBe([("Heat", 1995)]);
    }

    [Fact]
    public async Task Got_it_while_tmdb_is_down_still_says_which_wish_it_was()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var item = await shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        shelf.Online.Tmdb.FailWith = MetadataProblem.Unavailable;

        var result = (await shelf.LookupWishAsync(item.Id)).ShouldNotBeNull();

        result.Outcome.ShouldBe(LookupOutcome.Failed);
        result.Problem.ShouldBe(MetadataProblem.Unavailable);
        result.Wish!.Format.ShouldBe(MediaFormat.Uhd4K);
    }

    [Fact]
    public async Task Got_it_for_something_not_on_the_wishlist_finds_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();

        (await shelf.LookupWishAsync(Guid.NewGuid())).ShouldBeNull();
    }
}
