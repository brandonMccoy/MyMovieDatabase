using Bunit;
using MMDb.Contracts.Movies;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Edit → Find on TMDB: match a film already in the collection and review what changes.</summary>
public sealed class FindOnTmdbTests : ShelfTestContext
{
    // Entered by hand: no director, overview or TMDB link yet.
    private static SaveMovieRequest HandEnteredHeat => new()
    {
        Title = "Heat",
        Year = 1995,
        Formats = [new(MovieFormatKind.Dvd, Upc: "786936894424")],
    };

    private IRenderedComponent<MovieEditor> RenderEdit(Guid id)
    {
        Navigation.NavigateTo($"movies/{id}/edit");
        var page = Render<MovieEditor>(p => p.Add(x => x.Id, id));
        page.WaitForElement("#title");
        return page;
    }

    [Fact]
    public async Task Find_on_tmdb_searches_for_the_film_by_title_and_year()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        var film = await SeedAsync(HandEnteredHeat);
        var page = RenderEdit(film.Id);

        page.Find("button.find").Click();

        page.WaitForElement(".match");
        page.Find("h1").TextContent.ShouldBe("Find on TMDB");
        page.Find(".lookup-bar input").GetAttribute("value").ShouldBe("Heat 1995");
        page.FindAll(".match .mt").Select(m => m.TextContent).ShouldBe(["Heat"]);
        Online.Tmdb.Searches.ShouldBe([("Heat", 1995)]);
    }

    [Fact]
    public async Task Picking_a_match_fills_in_the_details_outlines_the_changes_and_keeps_the_formats()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var film = await SeedAsync(HandEnteredHeat);
        var page = RenderEdit(film.Id);
        page.Find("button.find").Click();

        page.WaitForElement(".match").Click();

        page.WaitForElement("#directors").GetAttribute("value").ShouldBe("Michael Mann");
        page.Find("#directors").ClassList.ShouldContain("edited");
        page.Find("#overview").ClassList.ShouldContain("edited");
        page.Find("#title").ClassList.ShouldNotContain("edited");
        page.Find("input[aria-label='DVD UPC']").GetAttribute("value").ShouldBe("786936894424");
        page.Find(".topbar button.primary").TextContent.ShouldBe("Save changes");
    }

    [Fact]
    public async Task Saving_links_the_film_to_tmdb_and_downloads_its_cover()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var film = await SeedAsync(HandEnteredHeat);
        var page = RenderEdit(film.Id);
        page.Find("button.find").Click();
        page.WaitForElement(".match").Click();

        page.WaitForElement(".topbar button.primary").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe($"movies/{film.Id}"));
        var saved = (await Api.GetMovieAsync(film.Id, Ct))!;
        saved.TmdbId.ShouldBe(949);
        saved.Directors.ShouldBe(["Michael Mann"]);
        saved.CoverUrl.ShouldNotBeNull();
        saved.Formats.ShouldHaveSingleItem().Upc.ShouldBe("786936894424");
    }

    [Fact]
    public async Task Back_to_editing_changes_nothing()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var film = await SeedAsync(HandEnteredHeat);
        var page = RenderEdit(film.Id);
        page.Find("#rating").Change("PG");
        page.Find("button.find").Click();
        page.WaitForElement(".match");

        page.Find(".lookup-foot button").Click();

        page.WaitForElement("#rating").GetAttribute("value").ShouldBe("PG");
        page.Find("#directors").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_film_with_a_cover_shows_it_while_editing()
    {
        var film = await SeedAsync(HandEnteredHeat with { TmdbId = 949, PosterPath = "/heat.jpg" });

        var page = RenderEdit(film.Id);

        page.Find(".detail .poster img").GetAttribute("src").ShouldBe(film.CoverUrl!.AbsoluteUri);
    }
}
