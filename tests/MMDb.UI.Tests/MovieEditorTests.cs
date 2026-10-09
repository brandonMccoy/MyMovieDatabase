using Bunit;
using MMDb.Contracts.Movies;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

public sealed class MovieEditorTests : ShelfTestContext
{
    private IRenderedComponent<MovieEditor> RenderNew()
    {
        Navigation.NavigateTo("movies/new");
        var page = Render<MovieEditor>();
        page.WaitForElement("#title");
        return page;
    }

    private IRenderedComponent<MovieEditor> RenderEdit(Guid id)
    {
        Navigation.NavigateTo($"movies/{id}/edit");
        var page = Render<MovieEditor>(p => p.Add(x => x.Id, id));
        page.WaitForElement("#title");
        return page;
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<MovieEditor> page) => page.Find(".topbar button.primary");

    private static void ToggleFormat(IRenderedComponent<MovieEditor> page, string name) =>
        page.FindAll(".fmt-toggle").Single(t => t.QuerySelector(".fmt")!.TextContent == name).Click();

    [Fact]
    public void Add_cannot_be_saved_without_a_title_and_a_format()
    {
        var page = RenderNew();
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();

        page.Find("#title").Input("Heat");
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();

        ToggleFormat(page, "DVD");
        SaveButton(page).HasAttribute("disabled").ShouldBeFalse();

        page.Find("#title").Input("   ");
        SaveButton(page).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Adding_saves_every_field_and_opens_the_new_film()
    {
        var page = RenderNew();

        page.Find("#title").Input("Heat");
        page.Find("#year").Change("1995");
        page.Find("#runtime").Change("170");
        page.Find("#rating").Change("R");
        page.Find("#directors").Change("Michael Mann");
        page.Find("#genre").Input("Crime, Drama,");
        page.Find("#overview").Change("A detective and a thief.");
        page.Find(".link-btn").Click();
        page.Find("[aria-label='Cast member 1 name']").Change("Al Pacino");
        page.Find("[aria-label='Cast member 1 role']").Change("Vincent Hanna");
        ToggleFormat(page, "4K UHD");
        page.Find("[aria-label='4K UHD UPC']").Change("786936894424");
        ToggleFormat(page, "Digital");
        page.Find("[aria-label='Digital file path']").Change(@"D:\Movies\Heat (1995).mkv");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("movies/"));
        var id = Guid.Parse(RelativeLocation["movies/".Length..]);
        var saved = await Api.GetMovieAsync(id, Ct);
        saved.ShouldNotBeNull();
        saved.Title.ShouldBe("Heat");
        saved.Year.ShouldBe(1995);
        saved.RuntimeMinutes.ShouldBe(170);
        saved.Rating.ShouldBe("R");
        saved.Directors.ShouldBe(["Michael Mann"]);
        saved.Genres.ShouldBe(["Crime", "Drama"]);
        saved.Overview.ShouldBe("A detective and a thief.");
        saved.Cast.ShouldBe([new CastMemberDto("Al Pacino", "Vincent Hanna")]);
        saved.Formats.Select(f => (f.Format, f.Upc, f.FilePath)).ShouldBe(
            [(MovieFormatKind.Uhd4K, "786936894424", null), (MovieFormatKind.Digital, null, @"D:\Movies\Heat (1995).mkv")]);
    }

    [Fact]
    public async Task A_rejected_barcode_is_shown_on_its_field_and_nothing_is_saved()
    {
        var page = RenderNew();
        page.Find("#title").Input("Heat");
        ToggleFormat(page, "Blu-ray");
        page.Find("[aria-label='Blu-ray UPC']").Change("123");

        SaveButton(page).Click();

        page.WaitForAssertion(() => page.Find("[data-field='BluRay.Upc']").TextContent.ShouldContain("Not a valid UPC"));
        page.Find("[aria-label='Blu-ray UPC']").ClassList.ShouldContain("invalid");
        RelativeLocation.ShouldBe("movies/new");
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Several_directors_are_separated_by_commas()
    {
        var page = RenderNew();
        page.Find("#title").Input("No Country for Old Men");
        page.Find("#directors").Change("Joel Coen,  Ethan Coen ,");
        ToggleFormat(page, "DVD");

        page.Find(".detail .pd").TextContent.ShouldBe("Joel Coen & Ethan Coen");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldStartWith("movies/"));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.Single().Directors.ShouldBe(["Joel Coen", "Ethan Coen"]);
    }

    [Fact]
    public void Genres_become_removable_tags_without_repeats()
    {
        var page = RenderNew();

        page.Find("#genre").Input("Crime,crime, Drama");
        page.Find("#genre").KeyDown("Enter");

        page.FindAll(".tag").Select(t => t.TextContent).ShouldBe(["Crime", "Drama"]);
        page.Find("button[aria-label='Remove Crime']").Click();
        page.FindAll(".tag").Select(t => t.TextContent).ShouldBe(["Drama"]);
    }

    [Fact]
    public async Task Genre_suggestions_come_from_the_collection()
    {
        await SeedAsync(Heat with { Genres = ["Crime", "Thriller"] });

        var page = RenderNew();

        page.FindAll("#genre-options option").Select(o => o.GetAttribute("value")).ShouldBe(["Crime", "Thriller"]);
    }

    [Fact]
    public async Task Edit_starts_from_the_saved_film()
    {
        var heat = await SeedAsync(Heat);

        var page = RenderEdit(heat.Id);

        page.Find("#title").GetAttribute("value").ShouldBe("Heat");
        page.Find("#year").GetAttribute("value").ShouldBe("1995");
        page.Find("#directors").GetAttribute("value").ShouldBe("Michael Mann");
        page.FindAll(".tag").Select(t => t.TextContent).ShouldBe(["Crime", "Drama"]);
        page.FindAll(".fmt-toggle.on .fmt").Select(f => f.TextContent).ShouldBe(["4K UHD", "DVD"]);
        page.Find("[aria-label='4K UHD UPC']").GetAttribute("value").ShouldBe("786936894424");
        page.Find(".topbar a.back").TextContent.ShouldBe("Heat");
    }

    [Fact]
    public async Task Changed_fields_are_outlined_until_saved()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderEdit(heat.Id);
        page.Find("#title").ClassList.ShouldNotContain("edited");

        page.Find("#title").Input("Heat (Director's Cut)");

        page.Find("#title").ClassList.ShouldContain("edited");
        page.Find("#year").ClassList.ShouldNotContain("edited");
    }

    [Fact]
    public void A_new_film_has_no_edited_outlines()
    {
        var page = RenderNew();

        page.Find("#title").Input("Heat");

        page.Find("#title").ClassList.ShouldNotContain("edited");
    }

    [Fact]
    public async Task Saving_an_edit_updates_the_film_and_keeps_fields_the_form_does_not_show()
    {
        var heat = await SeedAsync(Heat with { TmdbId = 949, TrailerYouTubeKey = "0xbBLJ1WGwQ" });
        Clock.Advance(TimeSpan.FromDays(40));
        var page = RenderEdit(heat.Id);

        page.Find("#runtime").Change("171");
        ToggleFormat(page, "DVD");
        ToggleFormat(page, "Blu-ray");
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/{heat.Id}"));
        var saved = await Api.GetMovieAsync(heat.Id, Ct);
        saved!.RuntimeMinutes.ShouldBe(171);
        saved.TmdbId.ShouldBe(949);
        saved.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ");
        saved.Formats.Select(f => (f.Format, f.AddedAt)).ShouldBe(
            [(MovieFormatKind.Uhd4K, Start), (MovieFormatKind.BluRay, Start.AddDays(40))]);
    }

    [Fact]
    public async Task Removing_a_cast_member_removes_them_on_save()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderEdit(heat.Id);

        page.Find("button[aria-label='Remove cast member 1']").Click();
        SaveButton(page).Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/{heat.Id}"));
        (await Api.GetMovieAsync(heat.Id, Ct))!.Cast.Select(c => c.Name).ShouldBe(["Robert De Niro"]);
    }

    [Fact]
    public async Task A_change_made_elsewhere_meanwhile_is_reported_and_can_be_loaded()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderEdit(heat.Id);
        await Api.UpdateMovieAsync(heat.Id, heat.Version, Heat with { Title = "Heat (changed elsewhere)" }, Ct);

        page.Find("#title").Input("Heat (mine)");
        SaveButton(page).Click();

        page.WaitForAssertion(() => page.Find(".problem").TextContent.ShouldContain("changed somewhere else"));
        (await Api.GetMovieAsync(heat.Id, Ct))!.Title.ShouldBe("Heat (changed elsewhere)");

        page.Find(".problem button").Click();

        page.WaitForAssertion(() => page.Find("#title").GetAttribute("value").ShouldBe("Heat (changed elsewhere)"));
        page.FindAll(".problem").ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_returns_without_saving()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderEdit(heat.Id);

        page.Find("#title").Input("Something else");

        page.FindAll(".topbar a").Single(a => a.TextContent == "Cancel").GetAttribute("href").ShouldBe($"movies/{heat.Id}");
        (await Api.GetMovieAsync(heat.Id, Ct))!.Title.ShouldBe("Heat");
    }

    [Fact]
    public void Editing_a_missing_movie_says_so()
    {
        Navigation.NavigateTo($"movies/{Guid.NewGuid()}/edit");

        var page = Render<MovieEditor>(p => p.Add(x => x.Id, Guid.NewGuid()));

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Movie not found"));
    }
}
