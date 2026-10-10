using Bunit;
using MMDb.Application.Metadata;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Domain.Movies;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>The wishlist on the PC: the list, adding to it by search, and buying what is on it ("Got it").</summary>
public sealed class WishlistScreenTests : ShelfTestContext
{
    private static MovieDetails Dune => new() { Title = "Dune", Year = 2021, Directors = ["Denis Villeneuve"], TmdbId = 438631 };

    private void TmdbKnowsTheHeats() => Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);

    private async Task<WishlistMovieDto> WantAsync(MovieDetails film, MovieFormatKind format)
    {
        var response = await Api.WantMovieAsync(new()
        {
            Title = film.Title,
            Year = film.Year,
            Directors = film.Directors,
            TmdbId = film.TmdbId,
            TrailerYouTubeKey = film.TrailerYouTubeKey,
            PosterPath = film.PosterPath,
            Format = format,
        }, Ct);
        response.Status.ShouldBe(SaveStatus.Saved);
        return response.Movie!;
    }

    private async Task<IReadOnlyList<WishlistMovieDto>> WishlistAsync() => (await Api.ListWishlistAsync(cancellationToken: Ct)).Movies;

    private static SaveMovieRequest HeatOn(params MovieFormatKind[] formats) =>
        new() { Title = "Heat", Year = 1995, TmdbId = 949, Formats = [.. formats.Select(f => new FormatRequest(f))] };

    private IRenderedComponent<WishlistPage> RenderWishlist(string query = "")
    {
        Navigation.NavigateTo("wishlist" + query);
        var page = Render<WishlistPage>();
        page.WaitForElement(".wl, .empty");
        return page;
    }

    private IRenderedComponent<WishlistAdd> RenderAdd()
    {
        Navigation.NavigateTo("wishlist/movies/new");
        var page = Render<WishlistAdd>();
        page.WaitForElement(".lookup");
        return page;
    }

    private IRenderedComponent<MovieEditor> RenderEditor(string location)
    {
        Navigation.NavigateTo(location);
        return Render<MovieEditor>();
    }

    private static void Search<TPage>(IRenderedComponent<TPage> page, string query)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        page.Find(".lookup-bar input").Input(query);
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match");
    }

    private static AngleSharp.Dom.IElement FormatChoice<TPage>(IRenderedComponent<TPage> page, string name)
        where TPage : Microsoft.AspNetCore.Components.IComponent =>
        page.FindAll(".want-format").Single(b => b.QuerySelector(".fmt")!.TextContent == name);

    private static void ToggleFormat(IRenderedComponent<MovieEditor> page, string name) =>
        page.FindAll(".fmt-toggle").Single(t => t.QuerySelector(".fmt")!.TextContent == name).Click();

    private static (string, string) WishHit(IRenderedComponent<MovieEditor> page) =>
        (Text(page.Find(".wish-hit .wh")), Text(page.Find(".wish-hit .ts")));

    // The text as read on screen: runs of whitespace from the markup collapse to one space.
    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    [Fact]
    public void An_empty_wishlist_says_so_and_offers_to_add()
    {
        var page = RenderWishlist();

        page.Find(".empty h2").TextContent.ShouldBe("No films on your wishlist");
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("wishlist/movies/new");
        page.Find(".topbar a.primary").GetAttribute("href").ShouldBe("wishlist/movies/new");
        Text(page.Find(".section-head .sub")).ShouldBe("0 items");
    }

    [Fact]
    public async Task Wanted_films_are_listed_newest_first_with_the_wanted_format_dashed()
    {
        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        Clock.Advance(TimeSpan.FromDays(40));
        await WantAsync(Dune, MovieFormatKind.BluRay);

        var page = RenderWishlist();

        Text(page.Find(".section-head .sub")).ShouldBe("2 items");
        var rows = page.FindAll(".wl-row");
        rows.Select(r => Text(r.QuerySelector(".wl-title")!)).ShouldBe(["Dune2021", "Heat1995"]);
        rows.Select(r => Text(r.QuerySelector(".wl-text .wl-sub")!)).ShouldBe(["Denis Villeneuve", "Michael Mann"]);
        rows.Select(r => Text(r.QuerySelector(".want")!)).ShouldBe(["Blu-ray", "4K UHD"]);
        page.FindAll(".want .fmt").ShouldAllBe(f => f.ClassList.Contains("want-b"));
        rows.Select(r => Text(r.QuerySelector(".wl-added")!)).ShouldBe(["Added Feb 2026", "Added Jan 2026"]);
        rows[1].QuerySelector("img")!.GetAttribute("src")!.ShouldStartWith(CoverBase.ToString());
    }

    [Fact]
    public async Task A_row_links_to_the_trailer_and_to_buying_it()
    {
        var heat = await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);

        var page = RenderWishlist();

        var trailer = page.Find(".wl-actions a.icon");
        trailer.GetAttribute("href").ShouldBe("https://www.youtube.com/watch?v=0xbBLJ1WGwQ");
        trailer.GetAttribute("aria-label").ShouldBe("Trailer for Heat");
        trailer.GetAttribute("target").ShouldBe("_blank");
        page.Find(".wl-actions a.got-it").GetAttribute("href").ShouldBe($"movies/new?wish={heat.Id}");
    }

    [Fact]
    public async Task A_film_owned_on_another_format_is_tagged_an_upgrade()
    {
        await SeedAsync(HeatOn(MovieFormatKind.Dvd, MovieFormatKind.Digital));
        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        await WantAsync(Dune, MovieFormatKind.Uhd4K);

        var page = RenderWishlist();

        page.FindAll(".wl-row").Select(r => r.QuerySelector(".wl-tag")?.TextContent).ShouldBe([null, "Upgrade · own DVD and Digital"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_search_filters_the_wishlist_and_says_when_nothing_matches()
    {
        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        await WantAsync(Dune, MovieFormatKind.Uhd4K);

        var page = RenderWishlist("?q=villeneuve");
        page.FindAll(".wl-title").Select(Text).ShouldBe(["Dune2021"]);
        Text(page.Find(".section-head .sub")).ShouldBe("2 items");

        page.Find(".topbar input").Input("jaws");
        RelativeLocation.ShouldBe("wishlist?q=jaws");

        var none = RenderWishlist("?q=jaws");
        none.Find(".empty h2").TextContent.ShouldBe("Nothing matches");
        none.Find(".empty a.btn").GetAttribute("href").ShouldBe("wishlist");
    }

    [Fact]
    public async Task Removing_a_film_takes_it_off_the_wishlist_at_once()
    {
        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        await WantAsync(Dune, MovieFormatKind.Uhd4K);
        var page = RenderWishlist();

        page.Find("button[aria-label='Remove Heat from the wishlist']").Click();

        page.WaitForAssertion(() => page.FindAll(".wl-title").Select(Text).ShouldBe(["Dune2021"]));
        (await WishlistAsync()).ShouldHaveSingleItem().Title.ShouldBe("Dune");
    }

    [Fact]
    public async Task The_wishlist_follows_what_is_wanted_or_bought_elsewhere_such_as_on_a_phone()
    {
        var page = RenderWishlist();

        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        page.WaitForAssertion(() => page.FindAll(".wl-title").Select(Text).ShouldBe(["Heat1995"]));

        await SeedAsync(HeatOn(MovieFormatKind.Uhd4K));
        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("No films on your wishlist"));
    }

    [Fact]
    public async Task Adding_to_the_wishlist_searches_like_add_movie_and_asks_only_for_the_format()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        page.Find("h1").TextContent.ShouldBe("Add to wishlist");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("wishlist");

        Search(page, "heat");
        page.FindAll(".match")[0].Click();

        page.WaitForElement(".want-step");
        Text(page.Find(".want-title")).ShouldBe("Heat1995");
        page.FindAll(".want-format").Select(b => Text(b)).ShouldBe(["4K UHD", "Blu-ray", "DVD", "Digital"]);
        page.FindAll(".want-format[aria-checked='true']").ShouldBeEmpty();
        page.Find(".want-actions .primary").HasAttribute("disabled").ShouldBeTrue("a format has to be chosen");
        page.FindAll("#title, .fmt-toggle").ShouldBeEmpty("there is no editor to fill in");

        FormatChoice(page, "Blu-ray").Click();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        var wanted = (await WishlistAsync()).ShouldHaveSingleItem();
        wanted.ShouldSatisfyAllConditions(
            w => w.Title.ShouldBe("Heat"),
            w => w.Year.ShouldBe(1995),
            w => w.Directors.ShouldBe(["Michael Mann"]),
            w => w.Format.ShouldBe(MovieFormatKind.BluRay),
            w => w.CoverUrl.ShouldNotBeNull(),
            w => w.TrailerUrl.ToString().ShouldEndWith("0xbBLJ1WGwQ"));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public void Back_from_the_format_returns_to_the_matches()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        Search(page, "heat");
        page.FindAll(".match")[1].Click();
        page.WaitForElement(".want-step");

        page.Find(".want-actions .ghost").Click();

        page.FindAll(".match").Count.ShouldBe(2);
        page.FindAll(".want-step").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_format_already_owned_cannot_be_wanted_and_another_one_is_an_upgrade()
    {
        TmdbKnowsTheHeats();
        await SeedAsync(HeatOn(MovieFormatKind.Dvd));
        var page = RenderAdd();
        Search(page, "heat");
        page.FindAll(".match")[0].Click();
        page.WaitForElement(".want-step");

        Text(FormatChoice(page, "DVD")).ShouldBe("DVDOwned");
        FormatChoice(page, "DVD").HasAttribute("disabled").ShouldBeTrue();
        Text(page.Find(".upgrade-note")).ShouldBe("You own it on DVD, so this is an upgrade.");

        FormatChoice(page, "4K UHD").Click();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        (await WishlistAsync()).ShouldHaveSingleItem().OwnedFormats.ShouldBe([MovieFormatKind.Dvd]);
    }

    [Fact]
    public async Task A_film_already_wanted_says_so_in_the_matches_and_picking_it_changes_the_format()
    {
        TmdbKnowsTheHeats();
        await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        var page = RenderAdd();

        Search(page, "heat");

        page.FindAll(".match").Select(m => m.QuerySelector(".wished") is { } wished ? Text(wished) : null).ShouldBe(["On your wishlist 4K", null]);
        page.FindAll(".match")[0].Click();
        page.WaitForElement(".want-step");
        Text(page.Find(".want-format[aria-checked='true']")).ShouldBe("4K UHD");
        Text(page.Find(".wanted-note")).ShouldBe("It is on your wishlist for 4K UHD. Choosing another format changes that.");
        Text(page.Find(".want-actions .primary")).ShouldBe("Update wishlist");

        FormatChoice(page, "DVD").Click();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        (await WishlistAsync()).ShouldHaveSingleItem().Format.ShouldBe(MovieFormatKind.Dvd);
    }

    [Fact]
    public async Task Got_it_opens_the_film_for_review_with_the_wanted_format_on_and_saving_moves_it_to_the_collection()
    {
        TmdbKnowsTheHeats();
        var wish = await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);

        var page = RenderEditor($"movies/new?wish={wish.Id}");

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Heat");
        page.Find("#runtime").GetAttribute("value").ShouldBe("170", "the film comes from TMDB in full");
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["4K UHD"]);
        WishHit(page).ShouldBe(("On your wishlist · wanted 4K UHD", "Saving removes it from your wishlist."));
        Text(page.Find(".topbar a.back")).ShouldBe("Wishlist");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("wishlist");
        Online.Tmdb.Searches.ShouldBeEmpty();

        page.Find(".topbar button.primary").Click();

        var saved = (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/{saved.Id}"));
        saved.Formats.ShouldBe([MovieFormatKind.Uhd4K]);
        (await WishlistAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bought_on_another_format_than_wanted_asks_whether_to_keep_it_on_the_wishlist(bool keep)
    {
        TmdbKnowsTheHeats();
        var wish = await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        var page = RenderEditor($"movies/new?wish={wish.Id}");
        page.WaitForElement("#title");

        ToggleFormat(page, "4K UHD");
        ToggleFormat(page, "DVD");

        WishHit(page).ShouldBe(("On your wishlist · wanted 4K UHD", "Keep it on the wishlist for 4K UHD"));
        page.Find(".wish-hit input").HasAttribute("checked").ShouldBeTrue("it stays unless told otherwise");
        if (!keep)
        {
            page.Find(".wish-hit input").Change(false);
        }

        page.Find(".topbar button.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("movies/"));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.Dvd]);
        var left = await WishlistAsync();
        if (keep)
        {
            left.ShouldHaveSingleItem().OwnedFormats.ShouldBe([MovieFormatKind.Dvd]);
        }
        else
        {
            left.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Adding_a_film_found_by_search_shows_that_it_is_on_the_wishlist()
    {
        TmdbKnowsTheHeats();
        await WantAsync(TestMovies.Heat1986OnTmdb, MovieFormatKind.BluRay);
        var page = RenderEditor("movies/new");
        page.WaitForElement(".lookup");

        Search(page, "heat");
        page.FindAll(".match")[0].Click();
        page.WaitForElement("#title");
        page.FindAll(".wish-hit").ShouldBeEmpty("Heat (1995) isn't wanted");

        page.Find(".review-bar button").Click();
        page.FindAll(".match")[1].Click();

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Heat");
        WishHit(page).ShouldBe(("On your wishlist · wanted Blu-ray", "Keep it on the wishlist for Blu-ray"));
        page.FindAll(".fmt-toggle.on").ShouldBeEmpty("found by search, nothing says which format was bought");

        ToggleFormat(page, "Blu-ray");
        WishHit(page).ShouldBe(("On your wishlist · wanted Blu-ray", "Saving removes it from your wishlist."));
    }

    [Fact]
    public async Task Got_it_while_tmdb_is_down_can_still_be_entered_by_hand()
    {
        var wish = await WantAsync(TestMovies.HeatOnTmdb, MovieFormatKind.Uhd4K);
        Online.Tmdb.FailWith = MetadataProblem.Unavailable;

        var page = RenderEditor($"movies/new?wish={wish.Id}");

        Text(page.WaitForElement(".notice")).ShouldStartWith("Couldn't reach TMDB.");
        page.Find(".lookup-bar input").GetAttribute("value").ShouldBe("Heat 1995");

        page.Find(".lookup-foot button").Click();

        page.Find("#title").GetAttribute("value").ShouldBe("Heat");
        page.Find("#year").GetAttribute("value").ShouldBe("1995");
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["4K UHD"]);
        WishHit(page).Item1.ShouldBe("On your wishlist · wanted 4K UHD");
    }

    [Fact]
    public void Got_it_for_something_no_longer_wanted_is_a_plain_add_movie()
    {
        var page = RenderEditor($"movies/new?wish={Guid.NewGuid()}");

        page.WaitForElement(".lookup");
        page.Find("h1").TextContent.ShouldBe("Add movie");
        Text(page.Find(".topbar a.back")).ShouldBe("Movies");
    }

    [Fact]
    public async Task A_film_being_added_can_go_to_the_wishlist_instead()
    {
        TmdbKnowsTheHeats();
        var page = RenderEditor("movies/new");
        page.WaitForElement(".lookup");
        Search(page, "heat");
        page.FindAll(".match")[0].Click();
        page.WaitForElement("#title");
        ToggleFormat(page, "Blu-ray");

        page.Find(".topbar button.to-wishlist").Click();

        page.Find("h1").TextContent.ShouldBe("Add to wishlist");
        Text(page.Find(".want-format[aria-checked='true']")).ShouldBe("Blu-ray", "the one format switched on is the one wanted");
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        (await WishlistAsync()).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            w => w.Title.ShouldBe("Heat"),
            w => w.Format.ShouldBe(MovieFormatKind.BluRay));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Back_from_the_wishlist_step_returns_to_the_editor_as_it_was()
    {
        TmdbKnowsTheHeats();
        var page = RenderEditor("movies/new");
        page.WaitForElement(".lookup");
        Search(page, "heat");
        page.FindAll(".match")[0].Click();
        page.WaitForElement("#title").Input("Heat (4K remaster)");

        page.Find(".topbar button.to-wishlist").Click();
        Text(page.Find(".want-title")).ShouldBe("Heat (4K remaster)1995");
        page.Find(".want-actions .ghost").Click();

        page.Find("#title").GetAttribute("value").ShouldBe("Heat (4K remaster)");
        (await WishlistAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Editing_a_saved_film_has_no_wishlist_button()
    {
        var movie = await SeedAsync(Heat);

        Navigation.NavigateTo($"movies/{movie.Id}/edit");
        var page = Render<MovieEditor>(p => p.Add(x => x.Id, movie.Id));

        page.WaitForElement("#title");
        page.FindAll(".to-wishlist").ShouldBeEmpty();
    }
}
