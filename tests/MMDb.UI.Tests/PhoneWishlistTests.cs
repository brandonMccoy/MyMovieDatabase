using Bunit;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Testing;
using MMDb.UI.Pages;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>The wishlist on the phone: the alert when something wanted is scanned, the heart, and the list.</summary>
public sealed class PhoneWishlistTests : PhoneTestContext
{
    private const string HeatUpc = "786936894424";

    public PhoneWishlistTests()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
    }

    private async Task<WishlistMovieDto> WantHeatAsync(MovieFormatKind format)
    {
        var response = await Api.WantMovieAsync(new()
        {
            Title = "Heat",
            Year = 1995,
            Directors = ["Michael Mann"],
            TmdbId = 949,
            PosterPath = "/heat.jpg",
            Format = format,
        }, Ct);
        response.Status.ShouldBe(SaveStatus.Saved);
        return response.Movie!;
    }

    private async Task<IReadOnlyList<WishlistMovieDto>> WishlistAsync() => (await Api.ListWishlistAsync(cancellationToken: Ct)).Movies;

    private async Task<ScanItem> ScanAsync(string code)
    {
        var (item, _) = Queue.Add(code);
        await item.Lookup;
        return item;
    }

    private IRenderedComponent<MovieEditor> ReviewFilm(ScanItem item)
    {
        Navigation.NavigateTo($"movies/new?scan={item.Id}");
        var page = Render<MovieEditor>();
        page.WaitForElement("#title");
        return page;
    }

    private static (string, string) WishHit(IRenderedComponent<MovieEditor> page) =>
        (Text(page.Find(".wish-hit .wh")), Text(page.Find(".wish-hit .ts")));

    [Fact]
    public async Task Scanning_something_wanted_says_so_over_the_camera()
    {
        await WantHeatAsync(MovieFormatKind.Uhd4K);
        Navigation.NavigateTo("scan");
        var page = Render<Scan>();

        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Text(page.Find(".toast")).ShouldBe("Heat (1995) is on your wishlist Wanted: 4K UHD · tap to review"));
        page.Find(".toast").ClassList.ShouldContain("wish");
        page.Find(".toast use").GetAttribute("href").ShouldBe("#i-heart");

        page.Find(".toast").Click();
        RelativeLocation.ShouldBe($"scan/review?item={Queue.Items.ShouldHaveSingleItem().Id}");
    }

    [Fact]
    public void Scanning_something_not_wanted_shows_the_usual_banner()
    {
        Navigation.NavigateTo("scan");
        var page = Render<Scan>();

        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Text(page.Find(".toast")).ShouldBe("Heat (1995) Tap to review"));
        page.Find(".toast").ClassList.ShouldNotContain("wish");
    }

    [Fact]
    public async Task Reviewing_a_wanted_scan_repeats_it_and_saving_takes_it_off_the_wishlist()
    {
        await WantHeatAsync(MovieFormatKind.Uhd4K);
        var page = ReviewFilm(await ScanAsync(HeatUpc));

        WishHit(page).ShouldBe(("On your wishlist · wanted 4K UHD", "Saving removes it from your wishlist."));

        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.Uhd4K]);
        (await WishlistAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scan_of_another_format_than_wanted_keeps_the_wish_unless_told_not_to()
    {
        await WantHeatAsync(MovieFormatKind.BluRay);
        var page = ReviewFilm(await ScanAsync(HeatUpc));

        WishHit(page).ShouldBe(("On your wishlist · wanted Blu-ray", "Keep it on the wishlist for Blu-ray"));
        page.Find(".wish-hit input").Change(false);
        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        (await WishlistAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_heart_sends_a_scan_to_the_wishlist_instead_of_the_collection()
    {
        var item = await ScanAsync(HeatUpc);
        var page = ReviewFilm(item);

        var heart = page.Find(".sticky .to-wishlist");
        heart.GetAttribute("aria-label").ShouldBe("Send to wishlist instead");
        heart.Click();

        Text(page.Find(".want-format[aria-checked='true']")).ShouldBe("4K UHD", "the scanned disc's format is the one suggested");
        page.FindAll(".sticky").ShouldBeEmpty();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        Queue.Items.ShouldBeEmpty();
        (await WishlistAsync()).ShouldHaveSingleItem().Format.ShouldBe(MovieFormatKind.Uhd4K);
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_the_heart_the_next_scan_is_reviewed()
    {
        Online.Barcodes.Add("883929301270", "Heat (1986) [DVD]");
        var first = await ScanAsync(HeatUpc);
        await ScanAsync("883929301270");
        var page = ReviewFilm(first);

        page.Find(".sticky .to-wishlist").Click();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("scan/review"));
        Queue.Items.ShouldHaveSingleItem().Code.ShouldBe("883929301270");
    }

    [Fact]
    public async Task The_phone_lists_the_wishlist_and_can_add_buy_and_remove()
    {
        var heat = await WantHeatAsync(MovieFormatKind.Uhd4K);
        Navigation.NavigateTo("wishlist");

        var page = Render<WishlistPage>();

        page.WaitForElement(".wl-row");
        page.Find(".appbar .ttl").TextContent.ShouldBe("Wishlist");
        page.Find(".appbar a.right").GetAttribute("href").ShouldBe("wishlist/movies/new");
        page.Find(".appbar a.right").GetAttribute("aria-label").ShouldBe("Add to wishlist");
        page.FindAll(".section-head").ShouldBeEmpty();
        page.Find(".topbar input").GetAttribute("placeholder").ShouldBe("Search wishlist…");
        Text(page.Find(".wl-title")).ShouldBe("Heat1995");
        page.Find(".wl-actions a.got-it").GetAttribute("href").ShouldBe($"movies/new?wish={heat.Id}");

        page.Find("button[aria-label='Remove Heat from the wishlist']").Click();
        page.Find(".wl-row .confirm button.danger").Click();

        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("No films on your wishlist"));
        (await WishlistAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_on_the_phone_saves_with_the_button_under_the_thumb()
    {
        var heat = await WantHeatAsync(MovieFormatKind.Dvd);
        Navigation.NavigateTo($"movies/new?wish={heat.Id}");
        var page = Render<MovieEditor>();
        page.WaitForElement("#title");

        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["DVD"]);
        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("movies/"));
        (await WishlistAsync()).ShouldBeEmpty();
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Formats.ShouldBe([MovieFormatKind.Dvd]);
    }

    [Fact]
    public async Task A_film_is_added_to_the_wishlist_on_the_phone_by_search()
    {
        Navigation.NavigateTo("wishlist/movies/new");
        var page = Render<WishlistAdd>();
        page.WaitForElement(".lookup");

        page.Find(".lookup-bar input").Input("heat 1986");
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match").Click();
        page.WaitForElement(".want-step");
        page.FindAll(".want-format").Single(b => Text(b) == "Blu-ray").Click();
        page.Find(".want-actions .primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        (await WishlistAsync()).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            w => w.Title.ShouldBe("Heat"),
            w => w.Year.ShouldBe(1986),
            w => w.Format.ShouldBe(MovieFormatKind.BluRay));
    }
}
