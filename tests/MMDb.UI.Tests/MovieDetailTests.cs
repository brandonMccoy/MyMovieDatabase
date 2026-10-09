using Bunit;
using MMDb.Contracts.Movies;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

public sealed class MovieDetailTests : ShelfTestContext
{
    private IRenderedComponent<MovieDetail> RenderDetail(Guid id)
    {
        var page = Render<MovieDetail>(p => p.Add(x => x.Id, id));
        page.WaitForElement("h1");
        return page;
    }

    [Fact]
    public async Task Shows_the_film_and_its_details()
    {
        var heat = await SeedAsync(Heat);

        var page = RenderDetail(heat.Id);

        page.Find("h1.title-lg").TextContent.ShouldBe("Heat1995");
        page.Find(".title-lg .yr").TextContent.ShouldBe("1995");
        page.Find(".byline").TextContent.ShouldBe("Directed by Michael Mann");
        page.Find(".rating").TextContent.ShouldBe("R");
        page.FindAll(".meta-row > span:not(.sep):not(.rating)").Select(s => s.TextContent).ShouldBe(["170 min", "Crime", "Drama"]);
        page.Find(".synopsis").TextContent.ShouldBe(Heat.Overview);
        page.FindAll(".fmts .fmt").Select(b => b.TextContent).ShouldBe(["4K UHD", "DVD"]);
    }

    [Fact]
    public async Task Several_directors_are_joined_naturally()
    {
        var film = await SeedAsync(Film("Everything Everywhere All at Once") with { Directors = ["Daniel Kwan", "Daniel Scheinert"] });

        RenderDetail(film.Id).Find(".byline").TextContent.ShouldBe("Directed by Daniel Kwan & Daniel Scheinert");
    }

    [Fact]
    public async Task Optional_parts_are_left_out_when_empty()
    {
        var film = await SeedAsync(Film("Untitled"));

        var page = RenderDetail(film.Id);

        page.Find("h1.title-lg").TextContent.ShouldBe("Untitled");
        page.FindAll(".byline").ShouldBeEmpty();
        page.FindAll(".rating").ShouldBeEmpty();
        page.FindAll(".synopsis").ShouldBeEmpty();
        page.Find(".tabs + *").TextContent.ShouldBe("No cast added.");
    }

    [Fact]
    public async Task The_cast_tab_lists_actors_with_their_roles()
    {
        var heat = await SeedAsync(Heat);

        var page = RenderDetail(heat.Id);

        page.FindAll(".person").Select(p => (p.QuerySelector(".avatar")!.TextContent, p.QuerySelector(".n")!.TextContent, p.QuerySelector(".r")!.TextContent))
            .ShouldBe([("AP", "Al Pacino", "Vincent Hanna"), ("RD", "Robert De Niro", "Neil McCauley")]);
    }

    [Fact]
    public async Task The_crew_tab_lists_the_directors()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderDetail(heat.Id);

        page.FindAll(".tabs button").Single(b => b.TextContent == "Crew").Click();

        page.FindAll(".person .n").Select(n => n.TextContent).ShouldBe(["Michael Mann"]);
        page.Find(".person .r").TextContent.ShouldBe("Director");
    }

    [Fact]
    public async Task Formats_owned_show_when_each_was_added_and_its_barcode_or_file()
    {
        var heat = await SeedAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K, Upc: "786936894424"), new(MovieFormatKind.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")] });

        var page = RenderDetail(heat.Id);

        page.FindAll(".panel .owned").Select(o => (o.QuerySelector(".fmt")!.TextContent, o.QuerySelector(".small")!.TextContent))
            .ShouldBe([("4K UHD", "Added Jan 2026"), ("Digital", "Added Jan 2026")]);
        page.FindAll(".panel .owned-code").Select(c => c.TextContent).ShouldBe(["UPC 786936894424", @"D:\Movies\Heat (1995).mkv"]);
        page.Find(".panel h3 a").GetAttribute("href").ShouldBe($"movies/{heat.Id}/edit");
    }

    [Fact]
    public async Task Watch_trailer_opens_youtube_outside_the_app()
    {
        var heat = await SeedAsync(Heat with { TrailerYouTubeKey = "0xbBLJ1WGwQ" });

        var trailer = RenderDetail(heat.Id).Find(".actions a.primary");

        trailer.TextContent.ShouldBe("Watch trailer");
        trailer.GetAttribute("href").ShouldBe("https://www.youtube.com/watch?v=0xbBLJ1WGwQ");
        trailer.GetAttribute("target").ShouldBe("_blank");
    }

    [Fact]
    public async Task Edit_goes_to_the_edit_screen()
    {
        var heat = await SeedAsync(Heat);

        RenderDetail(heat.Id).FindAll(".actions a").Single(a => a.TextContent == "Edit").GetAttribute("href").ShouldBe($"movies/{heat.Id}/edit");
    }

    [Fact]
    public void A_missing_movie_says_so()
    {
        var page = RenderDetail(Guid.NewGuid());

        page.Find("h1").TextContent.ShouldBe("Movie not found");
    }

    [Fact]
    public async Task Deleting_asks_first_then_removes_the_film_and_returns_to_the_library()
    {
        var heat = await SeedAsync(Heat);
        Navigation.NavigateTo($"movies/{heat.Id}");
        var page = RenderDetail(heat.Id);

        page.Find("button[aria-label='More']").Click();
        page.Find(".menu button").Click();
        (await Api.GetMovieAsync(heat.Id, Ct)).ShouldNotBeNull();
        page.Find(".confirm").TextContent.ShouldContain("Delete Heat?");
        page.FindAll(".confirm button").Single(b => b.TextContent == "Delete").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        (await Api.GetMovieAsync(heat.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_the_delete_keeps_the_film()
    {
        var heat = await SeedAsync(Heat);
        var page = RenderDetail(heat.Id);

        page.Find("button[aria-label='More']").Click();
        page.Find(".menu button").Click();
        page.FindAll(".confirm button").Single(b => b.TextContent == "Cancel").Click();

        page.FindAll(".confirm").ShouldBeEmpty();
        (await Api.GetMovieAsync(heat.Id, Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_cover_is_shown_when_there_is_one()
    {
        var heat = await SeedAsync(Heat with { TmdbId = 949, PosterPath = "/heat.jpg" });

        var poster = RenderDetail(heat.Id).Find(".detail .poster");

        poster.QuerySelector("img")!.GetAttribute("src").ShouldBe(new Uri(CoverBase, $"{heat.Id:N}-heat.jpg").AbsoluteUri);
        poster.QuerySelector("img")!.GetAttribute("alt").ShouldBe("");
    }
}
