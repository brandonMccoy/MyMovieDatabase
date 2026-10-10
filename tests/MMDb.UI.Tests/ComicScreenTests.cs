using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.Application.Metadata;
using MMDb.Application.Vault;
using MMDb.Contracts.Comics;
using MMDb.Domain.Comics;
using MMDb.Testing;
using MMDb.UI.Layout;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>The comics library: series runs with progress and missing issues, the other shelves, filters.</summary>
public sealed class ComicLibraryTests : ShelfTestContext
{
    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private void MetronKnowsSaga() =>
        Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));

    private IRenderedComponent<ComicLibrary> RenderLibrary(string query = "")
    {
        Navigation.NavigateTo("comics" + query);
        var page = Render<ComicLibrary>();
        page.WaitForElement("h1");
        return page;
    }

    [Fact]
    public void An_empty_collection_offers_to_add_a_comic()
    {
        var page = RenderLibrary();

        page.Find(".empty h2").TextContent.ShouldBe("No comics yet");
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("comics/new");
    }

    [Fact]
    public async Task Runs_show_their_progress_and_the_issues_missing_between()
    {
        MetronKnowsSaga();
        await SeedComicAsync(TestComics.SagaIssue("1") with { IsKeyIssue = true, KeyNote = "First issue" });
        await SeedComicAsync(TestComics.SagaIssue("2"));
        await SeedComicAsync(TestComics.Saga7);

        var page = RenderLibrary();

        var run = page.Find("section.series");
        Text(run.QuerySelector("h3")!).ShouldBe("Saga (2012)");
        run.QuerySelector(".pub")!.TextContent.ShouldBe("Image");
        Text(run.QuerySelector(".progress")!).ShouldBe("3 of 8");
        run.QuerySelector(".bar i")!.GetAttribute("style").ShouldBe("width:37.5%");
        run.QuerySelectorAll(".issue").Select(i => (Text(i.QuerySelector(".num")!), i.ClassList.Contains("missing"))).ShouldBe(
        [
            ("#1KEY", false), ("#2", false), ("#3–6", true), ("#7", false),
        ]);
        run.QuerySelector(".issue .sub2")!.TextContent.ShouldBe("First issue");
        Text(page.Find(".section-head .sub")).ShouldBe("3 issues · 1 series · 0 trades");
    }

    [Fact]
    public async Task Each_variant_owned_is_a_tile_of_its_own()
    {
        await SeedComicAsync(TestComics.Saga7);
        await SeedComicAsync(TestComics.Saga7 with { Variant = "Cover B", Barcode = "70985301744800721" });

        var page = RenderLibrary();

        page.FindAll(".issue").Select(i => (Text(i.QuerySelector(".num")!), i.QuerySelector(".sub2")?.TextContent)).ShouldBe(
            [("#7", null), ("#7", "Cover B")]);
    }

    [Fact]
    public async Task A_run_metron_doesnt_know_counts_its_issues()
    {
        await SeedComicAsync(TestComics.Daredevil181);

        var page = RenderLibrary();

        Text(page.Find(".series .progress")).ShouldBe("1 issue");
        page.FindAll(".bar").ShouldBeEmpty();
    }

    [Fact]
    public async Task Trades_have_their_own_shelf()
    {
        await SeedComicAsync(TestComics.SagaVolume1);
        await SeedComicAsync(TestComics.Saga7);
        var page = RenderLibrary();

        page.FindAll(".chip").Single(c => c.TextContent == "Trades & HCs").Click();

        RelativeLocation.ShouldBe("comics?shelf=Collected");
        var shelf = RenderLibrary("?shelf=Collected");
        shelf.FindAll(".issues.collected .issue .num a").Select(a => a.TextContent).ShouldBe(["Saga, Volume One"]);
    }

    [Fact]
    public async Task The_key_issues_chip_counts_them()
    {
        await SeedComicAsync(TestComics.Daredevil181);
        await SeedComicAsync(TestComics.Saga7);

        var page = RenderLibrary("?shelf=KeyIssues");

        Text(page.FindAll(".chip").Single(c => c.ClassList.Contains("on"))).ShouldBe("Key issues1");
        page.FindAll(".issue .num a").Select(a => a.TextContent).ShouldBe(["Daredevil #181"]);
    }

    [Fact]
    public async Task Search_and_publisher_narrow_the_shelf()
    {
        await SeedComicAsync(TestComics.Daredevil181);
        await SeedComicAsync(TestComics.Saga7);

        RenderLibrary("?q=miller").FindAll("section.series h3").Select(Text).ShouldBe(["Daredevil (1964)"]);
        RenderLibrary("?publisher=Image").FindAll("section.series h3").Select(Text).ShouldBe(["Saga (2012)"]);
        RenderLibrary("?q=nothing").Find(".empty h2").TextContent.ShouldBe("No comics match");
    }

    [Fact]
    public async Task The_sidebar_counts_comics_and_lights_up_on_comic_pages()
    {
        await SeedComicAsync(TestComics.Saga7);
        await SeedComicAsync(TestComics.SagaVolume1);
        Navigation.NavigateTo("comics");

        var layout = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

        layout.WaitForAssertion(() => layout.FindAll(".nav a.active").Select(a => Text(a)).ShouldBe(["Comics2"]));
    }
}

/// <summary>A comic's page: what it is, the copy owned, its series' progress, delete.</summary>
public sealed class ComicDetailTests : ShelfTestContext
{
    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private IRenderedComponent<ComicDetail> RenderDetail(Guid id)
    {
        var page = Render<ComicDetail>(p => p.Add(x => x.Id, id));
        page.WaitForElement("h1");
        return page;
    }

    [Fact]
    public async Task An_issue_shows_its_series_creators_and_copy()
    {
        Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7"]));
        var saga = await SeedComicAsync(TestComics.Saga7 with { Variant = "Cover B", IsKeyIssue = true, KeyNote = "1st The Will" });

        var page = RenderDetail(saga.Id);

        page.Find("h1").TextContent.ShouldBe("Saga #7");
        page.Find(".fmts .key").TextContent.ShouldBe("KEY");
        Text(page.Find(".detail > div:nth-child(2) > .byline")).ShouldBe("Saga (2012) · Image · November 2012");
        page.Find(".story").TextContent.ShouldBe("Chapter Seven");
        page.Find(".key-note").TextContent.ShouldBe("1st The Will");
        page.FindAll(".credits .byline").Select(Text).ShouldBe(["Written by Brian K. Vaughan", "Art by Fiona Staples"]);
        page.Find(".owned-copy .copy").TextContent.ShouldBe("Cover B");
        Text(page.Find(".owned-copy .owned-code")).ShouldBe("UPC 709853017448 00711");
        Text(page.Find(".run .progress")).ShouldBe("1 of 7 owned");
        page.Find(".open-series").GetAttribute("href").ShouldBe("comics?q=Saga");
        page.Find(".poster img").GetAttribute("src").ShouldBe(saga.CoverUrl!.AbsoluteUri);
    }

    [Fact]
    public async Task A_trade_shows_its_format_and_isbn_and_no_series_panel()
    {
        var trade = await SeedComicAsync(TestComics.SagaVolume1);

        var page = RenderDetail(trade.Id);

        page.Find("h1").TextContent.ShouldBe("Saga, Volume One");
        Text(page.Find(".detail > div:nth-child(2) > .byline")).ShouldBe("Trade paperback · Saga Vol. 1 · Image Comics");
        Text(page.Find(".owned-copy .owned-code")).ShouldBe("ISBN 9781607066019");
        page.FindAll(".run").ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_asks_first_then_goes_back_to_comics()
    {
        var saga = await SeedComicAsync(TestComics.Saga7);
        var page = RenderDetail(saga.Id);

        page.Find("button[aria-label=More]").Click();
        page.Find(".menu .danger").Click();
        Text(page.Find(".confirm div")).ShouldStartWith("Delete Saga #7?");
        page.Find(".confirm .btn.danger").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("comics"));
        (await Api.GetComicAsync(saga.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public void A_comic_that_is_gone_says_so()
    {
        RenderDetail(Guid.NewGuid()).Find("h1").TextContent.ShouldBe("Comic not found");
    }

    [Fact]
    public async Task With_a_vault_the_page_links_to_its_note()
    {
        (await VaultApi.SetFolderAsync(VaultFolder, Ct)).ShouldBeTrue();
        var saga = await SeedComicAsync(TestComics.Saga7);

        var page = RenderDetail(saga.Id);

        page.WaitForElement("a.vault").TextContent.Trim().ShouldBe("Comics/Saga (2012)/Saga 7.md");
    }
}

/// <summary>Add comic: search Metron or Open Library, review the match, save; edit and enter by hand.</summary>
public sealed class ComicEditorTests : ShelfTestContext
{
    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private IRenderedComponent<ComicEditor> RenderAdd()
    {
        Navigation.NavigateTo("comics/new");
        var page = Render<ComicEditor>();
        page.WaitForElement(".lookup");
        return page;
    }

    private static void Search(IRenderedComponent<ComicEditor> page, string query)
    {
        page.Find(".lookup-bar input").Input(query);
        page.Find("form.lookup-bar").Submit();
    }

    private static void Pick(IRenderedComponent<ComicEditor> page, int index)
    {
        page.WaitForElement(".match");
        page.FindAll(".match")[index].Click();
        page.WaitForElement("#series");
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<ComicEditor> page) => page.Find(".topbar button.primary");

    private static VariantFacts CoverB => new("Cover B", "70985301744800721", "https://static.metron.cloud/media/variants/saga-7-b.jpg");

    [Fact]
    public void Add_comic_opens_on_the_search()
    {
        var page = RenderAdd();

        page.Find("h1").TextContent.ShouldBe("Add comic");
        page.Find(".lookup-bar input").GetAttribute("placeholder").ShouldBe("Series and issue, barcode or ISBN…");
        page.FindAll(".topbar button.primary").ShouldBeEmpty();
    }

    [Fact]
    public async Task Matches_show_each_cover_and_which_are_owned()
    {
        Online.Metron.Add(TestComics.Saga7, CoverB);
        await SeedComicAsync(TestComics.Saga7);
        var page = RenderAdd();

        Search(page, "saga 7");

        page.WaitForElement(".match");
        page.FindAll(".match").Select(m => (Text(m.QuerySelector(".mt")!), Text(m.QuerySelector(".mm")!), m.QuerySelector(".mo")?.TextContent)).ShouldBe(
        [
            ("Saga #7", "Saga (2012) · Image · Nov 2012 · Brian K. Vaughan", "You own this copy"),
            ("Saga #7 · Cover B", "Saga (2012) · Image · Nov 2012 · Brian K. Vaughan", "In your collection: regular cover"),
        ]);
        page.FindAll(".match img").Select(i => i.GetAttribute("src")).ShouldBe([TestComics.Saga7.CoverUrl, CoverB.CoverUrl]);
    }

    [Fact]
    public async Task Picking_a_match_fills_in_the_editor_and_saving_adds_the_copy()
    {
        Online.Metron.Add(TestComics.Saga7, CoverB);
        var page = RenderAdd();
        Search(page, "saga 7");

        Pick(page, 1);

        Text(page.Find(".review-bar .src")).ShouldBe("From Metron");
        page.Find("#series").GetAttribute("value").ShouldBe("Saga");
        page.Find("#number").GetAttribute("value").ShouldBe("7");
        page.Find("#year").GetAttribute("value").ShouldBe("2012");
        page.Find("#cover-date").GetAttribute("value").ShouldBe("2012-11");
        page.Find("#variant").GetAttribute("value").ShouldBe("Cover B");
        page.Find("#barcode").GetAttribute("value").ShouldBe("70985301744800721");
        page.Find("#writers").GetAttribute("value").ShouldBe("Brian K. Vaughan");
        page.FindAll(".edited").ShouldBeEmpty();

        page.Find("#variant").Change("Cover B (Staples)");
        page.Find("#variant").ClassList.ShouldContain("edited");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("comics/"));
        var saved = (await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Issues }, Ct)).Comics.ShouldHaveSingleItem();
        saved.Variant.ShouldBe("Cover B (Staples)");
        saved.CoverUrl.ShouldNotBeNull();
    }

    [Fact]
    public async Task Saving_a_copy_already_owned_says_so_and_links_to_it()
    {
        Online.Metron.Add(TestComics.Saga7);
        var owned = await SeedComicAsync(TestComics.Saga7);
        var page = RenderAdd();
        Search(page, "saga 7");
        Pick(page, 0);

        Text(page.Find(".owned-note")).ShouldStartWith("You already own this copy.");
        SaveButton(page).Click();

        page.WaitForAssertion(() => Text(page.Find(".problem span")).ShouldBe("You already own this copy of Saga #7."));
        page.Find(".problem a").GetAttribute("href").ShouldBe($"comics/{owned.Id}");
    }

    [Fact]
    public async Task Another_variant_of_an_owned_issue_says_what_is_owned()
    {
        Online.Metron.Add(TestComics.Saga7, CoverB);
        await SeedComicAsync(TestComics.Saga7);
        var page = RenderAdd();
        Search(page, "saga 7");

        Pick(page, 1);

        Text(page.Find(".owned-note")).ShouldBe("You own the regular cover of this issue. Saving adds this copy.");
    }

    [Fact]
    public void An_isbn_finds_the_trade_and_the_editor_shows_trade_fields()
    {
        Online.Books.Add(TestComics.SagaVolume1);
        var page = RenderAdd();
        Search(page, "9781607066019");
        page.WaitForElement(".match");
        page.FindAll(".match")[0].Click();

        page.WaitForElement("#title");
        Text(page.Find(".review-bar .src")).ShouldBe("Best match · Open Library");
        page.Find("#title").GetAttribute("value").ShouldBe("Saga, Volume One");
        page.Find("#isbn").GetAttribute("value").ShouldBe("9781607066019");
        page.Find("#format").GetAttribute("value").ShouldBe(nameof(ComicFormatKind.TradePaperback));
        page.FindAll("#barcode").ShouldBeEmpty();
    }

    [Fact]
    public void Without_a_metron_token_the_search_says_how_to_add_one()
    {
        Online.Metron.FailWith = MetadataProblem.NotConfigured;
        var page = RenderAdd();

        Search(page, "saga 7");

        Text(page.WaitForElement(".notice")).ShouldContain("dotnet user-secrets set Metron:Token <token> --project src/MMDb.App");
    }

    [Fact]
    public void An_unknown_barcode_is_kept_when_entering_by_hand()
    {
        var page = RenderAdd();
        Search(page, "70985301744800711");
        Text(page.WaitForElement(".notice")).ShouldStartWith("This barcode isn't on Metron.");

        page.Find(".lookup-foot .link-btn").Click();

        page.Find("#barcode").GetAttribute("value").ShouldBe("70985301744800711");
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();
        page.Find("#series").Input("Saga");
        page.Find("#number").Input("7");
        SaveButton(page).HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void Typed_text_becomes_the_series_when_entering_by_hand()
    {
        var page = RenderAdd();
        Search(page, "Love and Rockets");
        page.WaitForElement(".notice");

        page.Find(".lookup-foot .link-btn").Click();

        page.Find("#series").GetAttribute("value").ShouldBe("Love and Rockets");
    }

    [Fact]
    public async Task Rejected_fields_are_marked_with_the_reason()
    {
        var page = RenderAdd();
        Search(page, "x");
        page.WaitForElement(".notice");
        page.Find(".lookup-foot .link-btn").Click();
        page.Find("#series").Input("Saga");
        page.Find("#number").Input("7");
        page.Find("#barcode").Change("123");

        SaveButton(page).Click();

        page.WaitForAssertion(() => page.Find("#barcode").ClassList.ShouldContain("invalid"));
        page.Find(".field-error").TextContent.ShouldStartWith("Not a valid barcode");
        (await Api.ListComicsAsync(new(), Ct)).Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Marking_a_key_issue_while_editing()
    {
        var saga = await SeedComicAsync(TestComics.Saga7);
        var page = Render<ComicEditor>(p => p.Add(x => x.Id, saga.Id));
        page.WaitForElement("#series");

        page.Find(".key-row .fmt-toggle").Click();
        page.Find(".key-row input").Change("1st Lying Cat");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"comics/{saga.Id}"));
        var edited = (await Api.GetComicAsync(saga.Id, Ct)).ShouldNotBeNull();
        edited.IsKeyIssue.ShouldBeTrue();
        edited.KeyNote.ShouldBe("1st Lying Cat");
    }

    [Fact]
    public async Task Editing_an_older_version_offers_the_latest()
    {
        var saga = await SeedComicAsync(TestComics.Saga7);
        var page = Render<ComicEditor>(p => p.Add(x => x.Id, saga.Id));
        page.WaitForElement("#series");
        await Api.UpdateComicAsync(saga.Id, saga.Version, Request(TestComics.Saga7 with { Variant = "Elsewhere" }), Ct);

        page.Find("#variant").Change("Mine");
        SaveButton(page).Click();

        page.WaitForAssertion(() => page.Find(".problem button").TextContent.ShouldBe("Load the latest version"));
    }

    [Fact]
    public async Task Find_on_metron_searches_for_the_issue_and_keeps_the_copy()
    {
        Online.Metron.Add(TestComics.Saga7);
        var mine = await SeedComicAsync(TestComics.Issue("Saga", "7", 2012) with { Variant = "Signed", Barcode = "70985301744800711" });
        var page = Render<ComicEditor>(p => p.Add(x => x.Id, mine.Id));
        page.WaitForElement("#series");

        page.Find(".find").Click();
        page.WaitForElement(".match").Click();

        page.WaitForElement("#series");
        Online.Metron.Searches[^1].ShouldBe(new IssueSearch { Series = "Saga", SeriesYear = 2012, Number = "7" });
        page.Find("#writers").GetAttribute("value").ShouldBe("Brian K. Vaughan");
        page.Find("#writers").ClassList.ShouldContain("edited");
        page.Find("#variant").GetAttribute("value").ShouldBe("Signed");
    }
}
