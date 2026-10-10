using Bunit;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Testing;
using MMDb.UI.Pages;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>The phone's scan screen: every barcode read joins a queue and is looked up by the PC meanwhile.</summary>
public sealed class ScanScreenTests : PhoneTestContext
{
    private const string HeatUpc = "786936894424";
    private const string UnknownUpc = "036000291452";

    public ScanScreenTests()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
        Online.Metron.Add(TestComics.Saga7);
    }

    private IRenderedComponent<Scan> RenderScan()
    {
        Navigation.NavigateTo("scan");
        return Render<Scan>();
    }

    private static string Toast(IRenderedComponent<Scan> page) => Text(page.Find(".toast"));

    [Fact]
    public async Task The_camera_runs_while_the_scan_screen_is_open()
    {
        var page = RenderScan();

        Scanner.Running.ShouldBe(ScanTarget.Product);
        page.FindAll(".reticle").ShouldHaveSingleItem();
        Text(page.Find(".cam-hint")).ShouldBe("Point at the barcode on the back");
        page.FindAll(".toast, .queue").ShouldBeEmpty();
        page.Find(".cam-bar a[aria-label='Close']").GetAttribute("href").ShouldBe("");

        await DisposeComponentsAsync();

        Scanner.Running.ShouldBeNull();
        Scanner.Stops.ShouldBe(1);
    }

    [Fact]
    public void A_scanned_film_joins_the_queue_and_shows_what_the_pc_found()
    {
        var page = RenderScan();

        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Heat (1995) Tap to review"));
        Text(page.Find(".queue")).ShouldBe("1 scanned · review");
        page.Find(".queue").GetAttribute("href").ShouldBe("scan/review");
        var item = Queue.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ProductKind.Movie);

        page.Find(".toast").Click();
        RelativeLocation.ShouldBe($"scan/review?item={item.Id}");
    }

    [Fact]
    public void Several_can_be_scanned_in_a_row()
    {
        var page = RenderScan();

        Scanner.Read(HeatUpc);
        Scanner.Read(TestComics.Saga7.Barcode!);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Saga #7 Tap to review"));
        Text(page.Find(".queue")).ShouldBe("2 scanned · review");
        Queue.Items.Select(i => i.Kind).ShouldBe([ProductKind.Movie, ProductKind.Comic]);
    }

    [Fact]
    public void A_barcode_held_in_view_counts_once_and_scanning_it_again_later_says_so()
    {
        var page = RenderScan();

        Scanner.Read(HeatUpc);
        Scanner.Read(HeatUpc);
        Clock.Advance(TimeSpan.FromSeconds(2));
        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Heat (1995) Tap to review"));
        Queue.Items.Count.ShouldBe(1);

        Clock.Advance(TimeSpan.FromSeconds(3));
        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Heat (1995) Already scanned · tap to review"));
        Queue.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void A_barcode_nobody_knows_can_still_be_reviewed_by_searching()
    {
        var page = RenderScan();

        Scanner.Read(UnknownUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Not found Tap to search by title"));
        page.Find(".toast").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public async Task Scanning_something_already_owned_says_so()
    {
        await SeedAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K, Upc: HeatUpc)] });
        var page = RenderScan();

        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("You already own Heat Tap to review"));
    }

    [Fact]
    public void A_scan_made_while_the_pc_is_away_waits_in_the_queue()
    {
        var page = RenderScan();
        Link.PcUp = false;

        Scanner.Read(HeatUpc);

        page.WaitForAssertion(() => Toast(page).ShouldBe("Couldn't ask the PC It stays in the queue to try again."));
        page.Find(".toast").HasAttribute("disabled").ShouldBeTrue();
        Queue.Items.ShouldHaveSingleItem().State.ShouldBe(ScanState.Failed);
    }

    [Fact]
    public void The_switch_says_what_is_being_scanned_when_the_code_cant_tell()
    {
        var page = RenderScan();
        page.FindAll(".type-switch button").Select(b => (b.TextContent, b.ClassList.Contains("on"))).ShouldBe(
            [("Movie", false), ("Comic", false), ("Auto", true)]);

        // A comic's UPC read without its add-on looks like a film's.
        page.FindAll(".type-switch button")[1].Click();
        Scanner.Read(TestComics.Saga7.Barcode![..12]);

        page.WaitForAssertion(() => Queue.Items.ShouldHaveSingleItem().Kind.ShouldBe(ProductKind.Comic));
        page.FindAll(".type-switch button.on").ShouldHaveSingleItem().TextContent.ShouldBe("Comic");
    }

    [Fact]
    public void The_keyboard_button_opens_typing_it_in_for_what_the_switch_says()
    {
        var page = RenderScan();
        var keyboard = () => page.Find(".cam-bar a[aria-label='Type it in']").GetAttribute("href");

        keyboard().ShouldBe("movies/new");
        page.FindAll(".type-switch button")[1].Click();
        keyboard().ShouldBe("comics/new");
        page.FindAll(".type-switch button")[0].Click();
        keyboard().ShouldBe("movies/new");
    }

    [Theory]
    [InlineData(ScannerStart.NotAllowed, "Shelf isn't allowed to use the camera Allow it in Android's settings, or type the code instead. Type it in")]
    [InlineData(ScannerStart.NoCamera, "No camera to scan with Type the code or the title instead. Type it in")]
    public async Task Without_the_camera_the_code_can_be_typed(ScannerStart answer, string message)
    {
        Scanner.Answer = answer;

        var page = RenderScan();

        Text(page.Find(".cam-off")).ShouldBe(message);
        page.Find(".cam-off a").GetAttribute("href").ShouldBe("movies/new");
        page.FindAll(".reticle").ShouldBeEmpty();

        await DisposeComponentsAsync();
        Scanner.Stops.ShouldBe(0, "it never started");
    }
}

/// <summary>Reviewing the phone's scans one by one in the editor, then on to the next.</summary>
public sealed class ScanReviewTests : PhoneTestContext
{
    private const string HeatUpc = "786936894424";
    private const string UnknownUpc = "036000291452";

    public ScanReviewTests()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
        Online.Metron.Add(TestComics.Saga7);
    }

    private async Task<ScanItem> ScanAsync(string code)
    {
        var (item, _) = Queue.Add(code);
        await item.Lookup;
        return item;
    }

    private IRenderedComponent<MovieEditor> ReviewFilm(ScanItem item)
    {
        Navigation.NavigateTo($"movies/new?scan={item.Id}");
        return Render<MovieEditor>();
    }

    private IRenderedComponent<ComicEditor> ReviewComic(ScanItem item)
    {
        Navigation.NavigateTo($"comics/new?scan={item.Id}");
        return Render<ComicEditor>();
    }

    private IRenderedComponent<ScanReview> RenderReview(string query = "")
    {
        Navigation.NavigateTo("scan/review" + query);
        return Render<ScanReview>();
    }

    [Fact]
    public async Task Review_goes_straight_to_the_oldest_scan_or_the_one_asked_for()
    {
        var film = await ScanAsync(HeatUpc);
        var comic = await ScanAsync(TestComics.Saga7.Barcode!);

        RenderReview();
        RelativeLocation.ShouldBe($"movies/new?scan={film.Id}");

        RenderReview($"?item={comic.Id}");
        RelativeLocation.ShouldBe($"comics/new?scan={comic.Id}");
    }

    [Fact]
    public async Task A_scanned_film_opens_filled_in_with_the_format_and_barcode_from_the_scan()
    {
        var film = await ScanAsync(HeatUpc);
        await ScanAsync(TestComics.Saga7.Barcode!);

        var page = ReviewFilm(film);

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Heat");
        Text(page.Find(".review-count")).ShouldBe("Review · 1 of 2");
        Text(page.Find(".topbar a.back")).ShouldBe("Scan");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("scan");
        Text(page.Find(".review-bar")).ShouldBe("Best match · TMDB Not it? 1 other match");
        page.Find("input[aria-label='4K UHD UPC']").GetAttribute("value").ShouldBe(HeatUpc);
        page.FindAll(".topbar .btn.primary").ShouldBeEmpty("on the phone the save button is at the bottom");
        Text(page.Find(".sticky .btn.primary")).ShouldBe("Add movie");
    }

    [Fact]
    public async Task Saving_a_reviewed_scan_adds_it_and_moves_on_to_the_next()
    {
        var film = await ScanAsync(HeatUpc);
        var comic = await ScanAsync(TestComics.Saga7.Barcode!);
        var page = ReviewFilm(film);
        page.WaitForElement("#title");

        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("scan/review"));
        var saved = (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        saved.Title.ShouldBe("Heat");
        saved.Formats.ShouldBe([MovieFormatKind.Uhd4K]);
        Queue.Items.ShouldHaveSingleItem().ShouldBe(comic);
    }

    [Fact]
    public async Task After_the_last_scan_the_library_shows_what_was_added()
    {
        var comic = await ScanAsync(TestComics.Saga7.Barcode!);
        var page = ReviewComic(comic);
        page.WaitForElement("#series").GetAttribute("value").ShouldBe("Saga");
        Text(page.Find(".review-count")).ShouldBe("Review · 1 of 1");

        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("comics"));
        Queue.Items.ShouldBeEmpty();
        var saved = (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldHaveSingleItem();
        (await Api.GetComicAsync(saved.Id, Ct))!.Barcode.ShouldBe(TestComics.Saga7.Barcode);
    }

    [Fact]
    public async Task Every_field_can_be_changed_before_saving()
    {
        var film = await ScanAsync(HeatUpc);
        var page = ReviewFilm(film);
        page.WaitForElement("#title");

        page.Find("#title").Input("Heat (Director's Definitive Edition)");
        page.Find("#title").ClassList.ShouldContain("edited");
        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat (Director's Definitive Edition)");
    }

    [Fact]
    public async Task A_scan_that_cant_be_saved_stays_in_the_queue_with_the_reason()
    {
        var film = await ScanAsync(HeatUpc);
        var page = ReviewFilm(film);
        page.WaitForElement("#title");

        page.Find("#year").Change("1500");
        page.Find(".sticky .btn.primary").Click();

        page.WaitForElement(".field-error");
        Queue.Items.ShouldHaveSingleItem();
        RelativeLocation.ShouldBe($"movies/new?scan={film.Id}");
    }

    [Fact]
    public async Task A_barcode_nobody_knows_opens_on_the_search_and_can_be_looked_up_as_a_comic_instead()
    {
        // A comic's UPC read without its add-on: taken for a film, and no film has it.
        var item = await ScanAsync(TestComics.Saga7.Barcode![..12]);
        var page = ReviewFilm(item);

        Text(page.WaitForElement(".notice")).ShouldStartWith("This barcode isn't in UPCitemdb.");
        Text(page.Find(".review-count")).ShouldBe("Review · 1 of 1");
        page.FindAll(".sticky").ShouldBeEmpty();

        page.Find("button.as-other").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"scan/review?item={item.Id}"));
        item.Kind.ShouldBe(ProductKind.Comic);
        RenderReview($"?item={item.Id}");
        RelativeLocation.ShouldBe($"comics/new?scan={item.Id}");
    }

    [Fact]
    public async Task A_scan_tmdb_has_no_film_for_opens_on_a_search_for_its_title_and_the_match_picked_gets_the_barcode()
    {
        Online.Barcodes.Add(UnknownUpc, "Heet Collectors Tin [Blu-ray]");
        var item = await ScanAsync(UnknownUpc);
        var page = ReviewFilm(item);

        page.WaitForElement(".lookup-bar input").GetAttribute("value").ShouldBe("Heet Collectors Tin");
        Text(page.Find(".code-chip")).ShouldBe($"UPC {UnknownUpc} · Heet Collectors Tin [Blu-ray]");

        page.Find(".lookup-bar input").Input("heat");
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match").Click();

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Heat");
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["Blu-ray"]);
        page.Find("input[aria-label='Blu-ray UPC']").GetAttribute("value").ShouldBe(UnknownUpc);

        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        var saved = await Api.GetMovieAsync((await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Id, Ct);
        saved!.Formats.Select(f => (f.Format, f.Upc)).ShouldBe([(MovieFormatKind.BluRay, UnknownUpc)]);
        Queue.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scan_nobody_knows_opens_on_a_blank_search_and_its_barcode_is_not_kept()
    {
        var item = await ScanAsync(UnknownUpc);
        var page = ReviewFilm(item);

        page.WaitForElement(".lookup-bar input").GetAttribute("value").ShouldBeNullOrEmpty();

        page.Find(".lookup-bar input").Input("heat");
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match").Click();

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Heat");
        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
        page.FindAll(".fmt-toggle").First(t => t.QuerySelector(".fmt")!.TextContent == "DVD").Click();
        page.Find("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_scan_whose_product_names_a_title_but_no_format_fills_the_search_and_drops_the_barcode()
    {
        Online.Barcodes.Add(UnknownUpc, "Heet Collectors Tin");
        var item = await ScanAsync(UnknownUpc);
        var page = ReviewFilm(item);

        page.WaitForElement(".lookup-bar input").GetAttribute("value").ShouldBe("Heet Collectors Tin");

        page.Find(".lookup-bar input").Input("heat");
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match").Click();

        page.WaitForElement("#title");
        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
        page.FindAll(".fmt-toggle").First(t => t.QuerySelector(".fmt")!.TextContent == "Blu-ray").Click();
        page.Find("input[aria-label='Blu-ray UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_film_scanned_as_a_comic_can_be_looked_up_as_a_film_instead()
    {
        Queue.Mode = ScanMode.Comic;
        var item = await ScanAsync(HeatUpc);
        var page = ReviewComic(item);
        page.WaitForElement(".lookup");

        page.Find("button.as-other").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"scan/review?item={item.Id}"));
        item.Kind.ShouldBe(ProductKind.Movie);
        item.Title.ShouldBe("Heat (1995)");
    }

    [Fact]
    public async Task A_scan_can_be_removed_without_saving()
    {
        var film = await ScanAsync(HeatUpc);
        var comic = await ScanAsync(TestComics.Saga7.Barcode!);
        var page = ReviewFilm(film);
        page.WaitForElement("#title");

        page.Find(".topbar button.skip").Click();

        RelativeLocation.ShouldBe("scan/review");
        Queue.Items.ShouldHaveSingleItem().ShouldBe(comic);
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();

        var next = ReviewComic(comic);
        next.WaitForElement("#series");
        next.Find(".topbar button.skip").Click();
        RelativeLocation.ShouldBe("scan", "nothing left to review");
    }

    [Fact]
    public async Task Something_already_owned_says_so_and_is_removed_rather_than_saved()
    {
        await SeedAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K, Upc: HeatUpc)] });
        var item = await ScanAsync(HeatUpc);

        var page = ReviewFilm(item);

        Text(page.WaitForElement(".notice.owned-barcode")).ShouldStartWith("You already own this: Heat");
        page.FindAll(".sticky").ShouldBeEmpty();
        page.FindAll(".topbar button.skip").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Scans_the_pc_hasnt_answered_for_are_listed_and_can_be_tried_again()
    {
        Link.PcUp = false;
        var item = await ScanAsync(HeatUpc);

        var page = RenderReview();

        RelativeLocation.ShouldBe("scan/review");
        Text(page.Find(".scans li")).ShouldBe($"{HeatUpc} Couldn't ask the PCTry again");

        Link.PcUp = true;
        page.Find(".scans button.btn.sm:not(.ghost)").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/new?scan={item.Id}"));
    }

    [Fact]
    public async Task A_waiting_scan_can_be_dropped_from_the_list()
    {
        Link.PcUp = false;
        await ScanAsync(HeatUpc);
        var page = RenderReview();

        page.Find($".scans button[aria-label='Remove {HeatUpc}']").Click();

        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("Nothing waiting"));
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("scan");
        Queue.Items.ShouldBeEmpty();
    }

    [Fact]
    public void A_review_link_to_a_scan_that_is_gone_is_just_the_add_screen()
    {
        Navigation.NavigateTo($"movies/new?scan={Guid.NewGuid()}");

        var page = Render<MovieEditor>();

        page.WaitForElement(".lookup");
        page.FindAll(".review-count, button.skip, button.as-other").ShouldBeEmpty();
        Text(page.Find(".topbar a.back")).ShouldBe("Movies");
    }

    [Fact]
    public async Task Typing_it_in_on_the_phone_is_the_same_add_screen_with_a_way_over_to_comics()
    {
        Navigation.NavigateTo("movies/new");
        var page = Render<MovieEditor>();
        page.WaitForElement(".lookup");
        page.Find(".lookup-foot a.link-btn").GetAttribute("href").ShouldBe("comics/new");

        page.Find(".lookup-bar input").Input("heat 1995");
        page.Find("form.lookup-bar").Submit();
        page.WaitForElement(".match").Click();
        page.WaitForElement("#title");
        page.FindAll(".fmt-toggle").Single(t => t.QuerySelector(".fmt")!.TextContent == "DVD").Click();
        page.Find(".sticky .btn.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldNotBe("movies/new"));
        var saved = (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        RelativeLocation.ShouldBe($"movies/{saved.Id}");
    }

    [Fact]
    public void Typing_a_comic_in_on_the_phone_offers_the_way_over_to_films()
    {
        Navigation.NavigateTo("comics/new");

        var page = Render<ComicEditor>();

        page.WaitForElement(".lookup");
        page.Find(".lookup-foot a.link-btn").GetAttribute("href").ShouldBe("movies/new");
    }
}
