using Bunit;
using MMDb.Application.Metadata;
using MMDb.Contracts.Movies;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Add movie: search TMDB by title or UPC, review the match in the editor, save.</summary>
public sealed class AddMovieLookupTests : ShelfTestContext
{
    private const string HeatUpc = "786936894424";

    private void TmdbKnowsTheHeats() =>
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb).Add(TestMovies.HeatAndDustOnTmdb);

    private IRenderedComponent<MovieEditor> RenderAdd()
    {
        Navigation.NavigateTo("movies/new");
        var page = Render<MovieEditor>();
        page.WaitForElement(".lookup");
        return page;
    }

    private static void Search(IRenderedComponent<MovieEditor> page, string query)
    {
        page.Find(".lookup-bar input").Input(query);
        page.Find("form.lookup-bar").Submit();
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Matches(IRenderedComponent<MovieEditor> page)
    {
        page.WaitForElement(".match");
        return page.FindAll(".match");
    }

    private static void Pick(IRenderedComponent<MovieEditor> page, int index)
    {
        Matches(page)[index].Click();
        page.WaitForElement("#title");
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<MovieEditor> page) => page.Find(".topbar button.primary");

    private static void ToggleFormat(IRenderedComponent<MovieEditor> page, string name) =>
        page.FindAll(".fmt-toggle").Single(t => t.QuerySelector(".fmt")!.TextContent == name).Click();

    private static string? SearchBox(IRenderedComponent<MovieEditor> page) => page.Find(".lookup-bar input").GetAttribute("value");

    private static string Notice(IRenderedComponent<MovieEditor> page) => Text(page.WaitForElement(".notice"));

    // The text as read on screen: runs of whitespace from the markup collapse to one space.
    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    [Fact]
    public void Add_movie_opens_on_the_search()
    {
        var page = RenderAdd();

        page.Find("h1").TextContent.ShouldBe("Add movie");
        page.Find(".lookup-bar input").GetAttribute("placeholder").ShouldBe("Title or UPC…");
        page.FindAll(".topbar button.primary").ShouldBeEmpty();
        page.Find(".lookup-bar button").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Matches_show_year_director_and_runtime_best_first()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();

        Search(page, "heat");

        Matches(page).Select(m => (m.QuerySelector(".mt")!.TextContent, m.QuerySelector(".mm")!.TextContent)).ShouldBe(
        [
            ("Heat", "1995 · Michael Mann · 170 min"),
            ("Heat", "1986 · Dick Richards"),
            ("Heat and Dust", "1983 · James Ivory"),
        ]);
        page.FindAll(".match img").Select(i => i.GetAttribute("src")).ShouldBe(
            ["https://image.tmdb.org/t/p/w92/heat.jpg", "https://image.tmdb.org/t/p/w92/heat86.jpg"]);
    }

    [Fact]
    public void Picking_a_match_fills_in_the_editor_for_review()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        Search(page, "heat");

        Pick(page, 0);

        page.Find("#title").GetAttribute("value").ShouldBe("Heat");
        page.Find("#year").GetAttribute("value").ShouldBe("1995");
        page.Find("#runtime").GetAttribute("value").ShouldBe("170");
        page.Find("#rating").GetAttribute("value").ShouldBe("R");
        page.Find("#directors").GetAttribute("value").ShouldBe("Michael Mann");
        page.FindAll(".tag").Select(t => t.TextContent).ShouldBe(["Crime", "Drama", "Thriller"]);
        page.Find("#overview").GetAttribute("value").ShouldBe(TestMovies.Heat.Overview);
        page.FindAll(".cast-row input[placeholder=Name]").Select(i => i.GetAttribute("value")).ShouldBe(["Al Pacino", "Robert De Niro"]);
        page.Find(".crew-kept").TextContent.ShouldBe($"{TestMovies.HeatCrew.Count} crew credits from TMDB are kept with this film.");
        page.Find(".review-bar .src").TextContent.ShouldBe("Best match · TMDB");
        page.Find(".review-bar button").TextContent.ShouldBe("Not it? 2 other matches");
        page.Find(".detail .poster img").GetAttribute("src").ShouldBe("https://image.tmdb.org/t/p/w342/heat.jpg");
        page.FindAll(".edited").ShouldBeEmpty();
    }

    [Fact]
    public void Fields_changed_from_the_tmdb_data_are_outlined()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        Search(page, "heat");
        Pick(page, 0);

        page.Find("#title").Input("Heat (Director's Definitive Edition)");
        page.Find("#rating").Change("R (restricted)");

        page.Find("#title").ClassList.ShouldContain("edited");
        page.Find("#rating").ClassList.ShouldContain("edited");
        page.Find("#year").ClassList.ShouldNotContain("edited");
    }

    [Fact]
    public async Task Saving_a_match_adds_the_film_with_its_tmdb_link_cover_and_trailer()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        Search(page, "heat");
        Pick(page, 0);

        ToggleFormat(page, "Blu-ray");
        SaveButton(page).TextContent.ShouldBe("Add movie");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("movies/"));
        var saved = (await Api.GetMovieAsync(Guid.Parse(RelativeLocation["movies/".Length..]), Ct)).ShouldNotBeNull();
        saved.TmdbId.ShouldBe(949);
        saved.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ");
        saved.PosterPath.ShouldBe("/heat.jpg");
        saved.Crew.Select(c => (c.Name, c.Job, c.Department)).ShouldBe(TestMovies.HeatCrew.Select(c => (c.Name, c.Job, c.Department)));
        saved.CoverUrl.ShouldBe(new Uri(CoverBase, $"{saved.Id:N}-heat.jpg"));
        saved.Formats.Select(f => f.Format).ShouldBe([MovieFormatKind.BluRay]);
        Online.Covers.Files.Keys.ShouldBe([$"{saved.Id:N}-heat.jpg"]);
    }

    [Fact]
    public void Not_it_goes_back_to_the_matches_and_another_pick_keeps_the_formats()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();
        Search(page, "heat");
        Pick(page, 0);
        ToggleFormat(page, "DVD");

        page.Find(".review-bar button").Click();
        Pick(page, 1);

        page.Find("#year").GetAttribute("value").ShouldBe("1986");
        page.Find("#directors").GetAttribute("value").ShouldBe("Dick Richards");
        page.Find(".review-bar .src").TextContent.ShouldBe("From TMDB");
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["DVD"]);
        Online.Tmdb.Searches.Count.ShouldBe(1);
    }

    [Fact]
    public void A_scanned_barcode_switches_on_its_format_with_the_upc()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heat (1995) [4K UHD + Blu-ray]");
        var page = RenderAdd();

        Search(page, HeatUpc);

        page.WaitForElement(".code-chip").TextContent.ShouldBe($"UPC {HeatUpc} · Heat (1995) [4K UHD + Blu-ray]");
        Pick(page, 0);
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["4K UHD"]);
        page.Find("input[aria-label='4K UHD UPC']").GetAttribute("value").ShouldBe(HeatUpc);
    }

    [Fact]
    public void A_barcode_whose_product_names_no_format_is_not_kept()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heat (1995)");
        var page = RenderAdd();
        Search(page, HeatUpc);
        Pick(page, 0);

        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
        ToggleFormat(page, "Blu-ray");

        page.Find("input[aria-label='Blu-ray UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void After_a_barcode_the_search_goes_on_by_the_title_its_product_names()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heat (1995) [4K UHD + Blu-ray]");
        var page = RenderAdd();

        Search(page, HeatUpc);

        Matches(page).Count.ShouldBe(1);
        SearchBox(page).ShouldBe("Heat");
    }

    [Fact]
    public void A_barcode_tmdb_has_no_film_for_is_kept_for_the_match_found_by_title()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heet Collectors Tin [Blu-ray]");
        var page = RenderAdd();

        Search(page, HeatUpc);

        Notice(page).ShouldBe(
            "No films found on TMDB for “Heet Collectors Tin [Blu-ray]”. Check the title and search again; the match you pick gets the UPC on Blu-ray.");
        SearchBox(page).ShouldBe("Heet Collectors Tin");

        Search(page, "heat");
        Matches(page).Count.ShouldBe(3);
        Text(page.Find(".code-chip")).ShouldBe($"UPC {HeatUpc} · kept for Blu-ray");
        Pick(page, 0);

        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["Blu-ray"]);
        page.Find("input[aria-label='Blu-ray UPC']").GetAttribute("value").ShouldBe(HeatUpc);
    }

    [Fact]
    public void A_barcode_with_a_title_but_no_format_fills_the_search_and_is_not_kept()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heet Collectors Tin");
        var page = RenderAdd();

        Search(page, HeatUpc);

        Notice(page).ShouldBe("No films found on TMDB for “Heet Collectors Tin”. Check the title and search again.");
        SearchBox(page).ShouldBe("Heet Collectors Tin");

        Search(page, "heat");
        Matches(page).Count.ShouldBe(3);
        page.FindAll(".code-chip").ShouldBeEmpty();
        Pick(page, 0);

        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
        ToggleFormat(page, "DVD");
        page.Find("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Looking_up_another_barcode_lets_go_of_the_one_kept()
    {
        TmdbKnowsTheHeats();
        Online.Barcodes.Add(HeatUpc, "Heet Collectors Tin [Blu-ray]");
        var page = RenderAdd();
        Search(page, HeatUpc);
        Notice(page).ShouldStartWith("No films found");

        Search(page, "036000291452");
        page.WaitForAssertion(() => Notice(page).ShouldStartWith("This barcode isn't in UPCitemdb."));
        Search(page, "heat");
        Pick(page, 0);

        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_film_already_owned_gets_the_new_format_added()
    {
        TmdbKnowsTheHeats();
        var owned = await SeedAsync(Heat with { TmdbId = 949, Formats = [new(MovieFormatKind.Dvd)] });
        var page = RenderAdd();
        Search(page, "heat 1995");

        Matches(page)[0].QuerySelector(".mo")!.TextContent.ShouldContain("In your collection");
        Pick(page, 0);
        Text(page.Find(".owned-note")).ShouldContain("Switch on a format you don't own yet to add it.");
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();

        ToggleFormat(page, "4K UHD");
        Text(page.Find(".owned-note")).ShouldContain("You already own this film. Saving adds 4K UHD to it.");
        SaveButton(page).TextContent.ShouldBe("Add 4K UHD format");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/{owned.Id}"));
        (await Api.GetMovieAsync(owned.Id, Ct))!.Formats.Select(f => f.Format).ShouldBe([MovieFormatKind.Uhd4K, MovieFormatKind.Dvd]);
        (await Api.ListMoviesAsync(new(), Ct)).Facets.FilmCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_barcode_already_on_the_shelf_links_to_the_film()
    {
        var owned = await SeedAsync(Heat);
        var page = RenderAdd();

        Search(page, HeatUpc);

        Notice(page).ShouldStartWith("You already own this:");
        page.Find(".notice a").GetAttribute("href").ShouldBe($"movies/{owned.Id}");
        Online.Barcodes.Lookups.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_barcode_says_so_leaves_the_search_blank_and_is_not_kept()
    {
        TmdbKnowsTheHeats();
        var page = RenderAdd();

        Search(page, HeatUpc);

        Notice(page).ShouldBe("This barcode isn't in UPCitemdb. Search for the title instead.");
        SearchBox(page).ShouldBeNullOrEmpty();

        Search(page, "heat");
        Pick(page, 0);
        page.FindAll(".fmt-toggle.on").ShouldBeEmpty();
        ToggleFormat(page, "DVD");
        page.Find("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Manual_entry_after_an_unknown_barcode_starts_blank()
    {
        var page = RenderAdd();
        Search(page, HeatUpc);
        Notice(page).ShouldContain("isn't in UPCitemdb");

        page.Find(".lookup-foot button").Click();
        ToggleFormat(page, "DVD");

        page.Find("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBeNullOrEmpty();
        page.Find("#title").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Manual_entry_after_a_barcode_with_a_format_switches_it_on_with_the_upc()
    {
        Online.Barcodes.Add(HeatUpc, "Some Home Movie [DVD]");
        var page = RenderAdd();
        Search(page, HeatUpc);
        Notice(page).ShouldStartWith("No films found");

        page.Find(".lookup-foot button").Click();

        page.WaitForElement("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBe(HeatUpc);
    }

    [Fact]
    public void Nothing_found_says_so_and_manual_entry_starts_with_the_typed_title()
    {
        var page = RenderAdd();
        Search(page, "Some Home Movie");

        Notice(page).ShouldBe("No films found on TMDB for “Some Home Movie”.");
        page.Find(".lookup-foot button").Click();

        page.WaitForElement("#title").GetAttribute("value").ShouldBe("Some Home Movie");
        page.FindAll(".review-bar").ShouldBeEmpty();
        page.FindAll(".edited").ShouldBeEmpty();
    }

    [Fact]
    public void Without_a_tmdb_key_the_search_sends_you_to_settings_to_add_one()
    {
        Online.Tmdb.FailWith = MetadataProblem.NotConfigured;
        var page = RenderAdd();

        Search(page, "heat");

        Notice(page).ShouldBe("TMDB isn't set up on this PC yet. Add your TMDB key in Settings.");
        page.Find(".notice a").GetAttribute("href").ShouldBe("settings");
        page.Find(".lookup-foot button").TextContent.ShouldBe("Enter manually");
    }

    [Fact]
    public void A_tmdb_key_that_is_turned_down_is_to_be_checked_in_settings()
    {
        Online.Tmdb.FailWith = MetadataProblem.KeyRejected;
        var page = RenderAdd();

        Search(page, "heat");

        Notice(page).ShouldBe("TMDB turned down the key on this PC. Check it in Settings.");
        page.Find(".notice a").GetAttribute("href").ShouldBe("settings");
    }

    [Fact]
    public void A_busy_barcode_database_explains_its_limits()
    {
        Online.Barcodes.FailWith = MetadataProblem.Busy;
        var page = RenderAdd();

        Search(page, HeatUpc);

        Notice(page).ShouldContain("UPCitemdb is busy right now.");
        Notice(page).ShouldContain("6 lookups a minute and 100 a day");
    }

    [Fact]
    public void Being_offline_is_explained()
    {
        Online.Tmdb.FailWith = MetadataProblem.Unavailable;
        var page = RenderAdd();

        Search(page, "heat");

        Notice(page).ShouldBe("Couldn't reach TMDB. Check the internet connection and try again.");
    }
}
