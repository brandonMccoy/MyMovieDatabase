using Bunit;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Domain.Comics;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Comics on the wishlist (PC): the Comics tab, missing issues, adding by search, and "Got it".</summary>
public sealed class WishlistComicScreenTests : ShelfTestContext
{
    public WishlistComicScreenTests()
    {
        Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));
        Online.Metron.Add(TestComics.Saga7).Add(TestComics.SagaIssue("4"));
        Online.Books.Add(TestComics.SagaVolume1);
    }

    private static WantComicRequest Wanted(ComicDetails comic) => new()
    {
        Format = (ComicFormatKind)(int)comic.Format,
        Series = comic.Series,
        SeriesYear = comic.SeriesYear,
        Number = comic.Number,
        Title = comic.Title,
        Publisher = comic.Publisher,
        Isbn = comic.Isbn,
        MetronId = comic.MetronId,
        MetronSeriesId = comic.MetronSeriesId,
        CoverSource = comic.CoverUrl,
    };

    private async Task<WishlistComicDto> WantAsync(ComicDetails comic)
    {
        var response = await Api.WantComicAsync(Wanted(comic), Ct);
        response.Status.ShouldBe(SaveStatus.Saved);
        return response.Comic!;
    }

    private async Task OwnSagaOneAndSevenAndWantTheRestAsync()
    {
        await SeedComicAsync(TestComics.SagaIssue("1"));
        await SeedComicAsync(TestComics.Saga7);
        (await Api.WantMissingIssuesAsync(new(1234, "2", "6"), Ct)).Status.ShouldBe(SaveStatus.Saved);
    }

    private async Task<IReadOnlyList<string>> WantedNamesAsync() => [.. (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.Select(c => c.DisplayName).Order()];

    private IRenderedComponent<WishlistPage> RenderWishlist(string query = "?tab=comics")
    {
        Navigation.NavigateTo("wishlist" + query);
        var page = Render<WishlistPage>();
        page.WaitForElement(".subtabs");
        return page;
    }

    private IRenderedComponent<ComicEditor> RenderEditor(string location)
    {
        Navigation.NavigateTo(location);
        return Render<ComicEditor>();
    }

    private static void Search<TPage>(IRenderedComponent<TPage> page, string query)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        page.Find(".lookup-bar input").Input(query);
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match");
    }

    private static (string, string) WishHit(IRenderedComponent<ComicEditor> page) =>
        (Text(page.Find(".wish-hit .wh")), Text(page.Find(".wish-hit .ts")));

    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    [Fact]
    public async Task Movies_and_comics_are_on_separate_tabs_with_their_counts()
    {
        await WantAsync(TestComics.Saga7);
        await Api.WantMovieAsync(new() { Title = "Dune", Year = 2021, Format = MovieFormatKind.Uhd4K }, Ct);

        var movies = RenderWishlist("?q=dune");

        movies.FindAll(".subtabs a").Select(a => (Text(a), a.GetAttribute("href"), a.ClassList.Contains("on"))).ShouldBe(
        [
            ("Movies1", "wishlist?q=dune", true),
            ("Comics1", "wishlist?q=dune&tab=comics", false),
        ]);
        movies.FindAll(".wl-title").Select(Text).ShouldBe(["Dune2021"]);
        movies.Find(".topbar a.primary").GetAttribute("href").ShouldBe("wishlist/movies/new");
        Text(movies.Find(".section-head .sub")).ShouldBe("2 items");

        var comics = RenderWishlist("?tab=comics");

        comics.FindAll(".subtabs a.on").ShouldHaveSingleItem().TextContent.ShouldBe("Comics1");
        comics.FindAll(".wl-title").Select(Text).ShouldBe(["Saga #7"]);
        comics.Find(".topbar a.primary").GetAttribute("href").ShouldBe("wishlist/comics/new");
    }

    [Fact]
    public async Task Wanted_comics_are_listed_newest_first_with_what_they_are()
    {
        var saga7 = await WantAsync(TestComics.Saga7);
        Clock.Advance(TimeSpan.FromDays(40));
        await WantAsync(TestComics.SagaVolume1);

        var page = RenderWishlist();

        var rows = page.FindAll(".wl-row");
        rows.Select(r => Text(r.QuerySelector(".wl-title")!)).ShouldBe(["Saga, Volume One", "Saga #7"]);
        rows.Select(r => Text(r.QuerySelector(".wl-text .wl-sub")!)).ShouldBe(["Image Comics · Trade paperback", "Image · 2012 series"]);
        rows.Select(r => Text(r.QuerySelector(".wl-added")!)).ShouldBe(["Added Feb 2026", "Added Jan 2026"]);
        page.FindAll(".wl-tag").ShouldBeEmpty();
        rows[1].QuerySelector("img")!.GetAttribute("src")!.ShouldStartWith(CoverBase.ToString());
        rows[1].QuerySelector("a.got-it")!.GetAttribute("href").ShouldBe($"comics/new?wish={saga7.Id}");
        page.FindAll(".wl-actions a.icon").ShouldBeEmpty("comics have no trailer");
    }

    [Fact]
    public void An_empty_comics_tab_says_how_to_fill_it()
    {
        var page = RenderWishlist();

        page.Find(".empty h2").TextContent.ShouldBe("No comics on your wishlist");
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("wishlist/comics/new");
    }

    [Fact]
    public async Task Every_issue_of_a_missing_tile_goes_on_the_wishlist()
    {
        await SeedComicAsync(TestComics.SagaIssue("1"));
        await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo("comics");
        var library = Render<ComicLibrary>();
        library.WaitForElement(".issue.missing");

        var want = library.Find(".issue.missing button.want-missing");
        want.TextContent.ShouldBe("+ Wishlist");
        want.GetAttribute("aria-label").ShouldBe("Add Saga #2–6 to the wishlist");
        want.Click();

        library.WaitForAssertion(() => Text(library.Find(".issue.missing .sub2")).ShouldBe("On wishlist"));
        library.FindAll("button.want-missing").ShouldBeEmpty();
        var wishlist = RenderWishlist();
        wishlist.FindAll(".wl-title").Select(Text).ShouldBe(["Saga #2", "Saga #3", "Saga #4", "Saga #5", "Saga #6"]);
        wishlist.FindAll(".subtabs a.on").ShouldHaveSingleItem().TextContent.ShouldBe("Comics5");
        var row = wishlist.Find(".wl-row");
        Text(row.QuerySelector(".wl-text .wl-sub")!).ShouldBe("Image · 2012 series");
        Text(row.QuerySelector(".wl-tag")!).ShouldBe("From missing issues");
        row.QuerySelector(".wl-tag")!.ClassList.ShouldContain("dim");
    }

    [Fact]
    public async Task A_run_nobody_lists_the_issues_of_has_no_missing_tiles_to_want()
    {
        await SeedComicAsync(TestComics.Daredevil181);
        Navigation.NavigateTo("comics");

        var library = Render<ComicLibrary>();

        library.WaitForElement(".issue");
        library.FindAll(".want-missing, .issue.missing").ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_comic_takes_it_off_the_wishlist_at_once()
    {
        await WantAsync(TestComics.Saga7);
        await WantAsync(TestComics.SagaVolume1);
        var page = RenderWishlist();

        page.Find("button[aria-label='Remove Saga #7 from the wishlist']").Click();

        page.WaitForAssertion(() => page.FindAll(".wl-title").Select(Text).ShouldBe(["Saga, Volume One"]));
        (await WantedNamesAsync()).ShouldBe(["Saga, Volume One"]);
    }

    [Fact]
    public async Task A_comic_found_by_search_goes_straight_on_the_wishlist()
    {
        Navigation.NavigateTo("wishlist/comics/new");
        var page = Render<WishlistAddComic>();
        page.WaitForElement(".lookup");
        page.Find("h1").TextContent.ShouldBe("Add to wishlist");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("wishlist?tab=comics");

        Search(page, "saga 7");
        page.Find(".match").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist?tab=comics"));
        (await WantedNamesAsync()).ShouldBe(["Saga #7"]);
        (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_book_is_wanted_by_its_isbn()
    {
        Navigation.NavigateTo("wishlist/comics/new");
        var page = Render<WishlistAddComic>();
        page.WaitForElement(".lookup");

        Search(page, "9781607066019");
        page.Find(".match").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist?tab=comics"));
        (await WantedNamesAsync()).ShouldBe(["Saga, Volume One"]);
    }

    [Fact]
    public async Task An_issue_already_owned_is_not_put_on_the_wishlist()
    {
        var owned = await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo("wishlist/comics/new");
        var page = Render<WishlistAddComic>();
        page.WaitForElement(".lookup");

        Search(page, "saga 7");
        page.Find(".match").Click();

        Text(page.WaitForElement(".problem span")).ShouldBe("You already own Saga #7.");
        page.Find(".problem a").GetAttribute("href").ShouldBe($"comics/{owned.Id}");
        RelativeLocation.ShouldBe("wishlist/comics/new");
        (await WantedNamesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Matches_say_what_is_wanted()
    {
        await OwnSagaOneAndSevenAndWantTheRestAsync();
        var page = RenderEditor("comics/new");
        page.WaitForElement(".lookup");

        Search(page, "saga 4");

        Text(page.Find(".match .wished")).ShouldBe("On your wishlist");
    }

    [Fact]
    public async Task Got_it_opens_the_issue_for_review_and_saving_moves_it_to_the_collection()
    {
        var wish = await WantAsync(TestComics.Saga7);

        var page = RenderEditor($"comics/new?wish={wish.Id}");

        page.WaitForElement("#series").GetAttribute("value").ShouldBe("Saga");
        page.Find("#number").GetAttribute("value").ShouldBe("7");
        WishHit(page).ShouldBe(("On your wishlist", "Saving removes it from your wishlist."));
        Text(page.Find(".topbar a.back")).ShouldBe("Wishlist");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("wishlist?tab=comics");
        page.FindAll(".to-wishlist").ShouldBeEmpty("it is on the wishlist already");

        page.Find(".topbar button.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("comics/"));
        (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #7");
        (await WantedNamesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_for_one_of_the_missing_issues_opens_that_issue_and_leaves_the_others_wanted()
    {
        await OwnSagaOneAndSevenAndWantTheRestAsync();
        var four = (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.Single(c => c.DisplayName == "Saga #4");

        var page = RenderEditor($"comics/new?wish={four.Id}");

        page.WaitForElement("#number").GetAttribute("value").ShouldBe("4");
        WishHit(page).ShouldBe(("On your wishlist", "Saving removes it from your wishlist."));

        page.Find(".topbar button.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("comics/"));
        (await WantedNamesAsync()).ShouldBe(["Saga #2", "Saga #3", "Saga #5", "Saga #6"]);
    }

    [Fact]
    public async Task Got_it_for_an_issue_metron_cant_find_can_be_entered_by_hand()
    {
        var wish = await WantAsync(TestComics.Issue("Daredevil", "181", 1964));

        var page = RenderEditor($"comics/new?wish={wish.Id}");

        page.WaitForElement(".lookup-bar input").GetAttribute("value").ShouldBe("Daredevil 1964 181");
        page.Find(".lookup-foot button").Click();

        page.Find("#series").GetAttribute("value").ShouldBe("Daredevil");
        page.Find("#number").GetAttribute("value").ShouldBe("181");
        WishHit(page).Item1.ShouldBe("On your wishlist");
    }

    [Fact]
    public async Task A_comic_being_added_can_go_to_the_wishlist_instead()
    {
        var page = RenderEditor("comics/new");
        page.WaitForElement(".lookup");
        Search(page, "saga 7");
        page.Find(".match").Click();
        page.WaitForElement("#series");
        page.FindAll(".wish-hit").ShouldBeEmpty();

        page.Find(".topbar button.to-wishlist").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist?tab=comics"));
        (await WantedNamesAsync()).ShouldBe(["Saga #7"]);
        (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sending_an_owned_issue_to_the_wishlist_says_it_is_owned()
    {
        await SeedComicAsync(TestComics.Saga7 with { Variant = "Cover B", Barcode = null });
        var page = RenderEditor("comics/new");
        page.WaitForElement(".lookup");
        Search(page, "saga 7");
        page.Find(".match").Click();
        page.WaitForElement("#series");

        page.Find(".topbar button.to-wishlist").Click();

        Text(page.WaitForElement(".problem span")).ShouldBe("You already own Saga #7.");
        (await WantedNamesAsync()).ShouldBeEmpty();
    }
}

/// <summary>Comics on the wishlist on the phone: the alert over the camera and the heart.</summary>
public sealed class PhoneWishlistComicTests : PhoneTestContext
{
    public PhoneWishlistComicTests()
    {
        Online.Metron.Add(TestComics.Saga7);
    }

    private async Task WantSaga7Async() => (await Api.WantComicAsync(
        new() { Series = "Saga", SeriesYear = 2012, Number = "7", MetronId = 4521, MetronSeriesId = 1234 }, Ct)).Status.ShouldBe(SaveStatus.Saved);

    [Fact]
    public async Task Scanning_a_wanted_comic_says_so_over_the_camera()
    {
        await WantSaga7Async();
        Navigation.NavigateTo("scan");
        var page = Render<Scan>();

        Scanner.Read(TestComics.Saga7.Barcode!);

        page.WaitForAssertion(() => Text(page.Find(".toast")).ShouldBe("Saga #7 is on your wishlist Tap to review"));
        page.Find(".toast").ClassList.ShouldContain("wish");
        page.Find(".toast use").GetAttribute("href").ShouldBe("#i-heart");
    }

    [Fact]
    public async Task Reviewing_a_wanted_comic_repeats_it_and_saving_takes_it_off_the_wishlist()
    {
        await WantSaga7Async();
        var (item, _) = Queue.Add(TestComics.Saga7.Barcode!);
        await item.Lookup;
        Navigation.NavigateTo($"comics/new?scan={item.Id}");
        var page = Render<ComicEditor>();
        page.WaitForElement("#series");

        Text(page.Find(".wish-hit .wh")).ShouldBe("On your wishlist");
        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("comics"));
        (await Api.ListWishlistAsync(cancellationToken: Ct)).ComicCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_heart_sends_a_scanned_comic_to_the_wishlist_instead()
    {
        var (item, _) = Queue.Add(TestComics.Saga7.Barcode!);
        await item.Lookup;
        Navigation.NavigateTo($"comics/new?scan={item.Id}");
        var page = Render<ComicEditor>();
        page.WaitForElement("#series");

        var heart = page.Find(".sticky .to-wishlist");
        heart.GetAttribute("aria-label").ShouldBe("Send to wishlist instead");
        heart.Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist?tab=comics"));
        Queue.Items.ShouldBeEmpty();
        (await Api.ListWishlistAsync(cancellationToken: Ct)).Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga #7");
        (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_phone_lists_wanted_comics_on_their_tab()
    {
        await WantSaga7Async();
        Navigation.NavigateTo("wishlist?tab=comics");

        var page = Render<WishlistPage>();

        page.WaitForElement(".wl-row");
        page.Find(".appbar a.right").GetAttribute("href").ShouldBe("wishlist/comics/new");
        Text(page.Find(".wl-title")).ShouldBe("Saga #7");
        page.FindAll(".subtabs a").Select(a => Text(a)).ShouldBe(["Movies0", "Comics1"]);
    }
}
