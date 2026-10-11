using Bunit;
using MMDb.Contracts.Movies;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>"Do I own…?" on the phone: one list across films, comics and the wishlist.</summary>
public sealed class PhoneOwnSearchTests : PhoneTestContext
{
    private static (string? Href, string Title, string Sub, string Verdict) Row(AngleSharp.Dom.IElement row) =>
        (row.GetAttribute("href"), row.QuerySelector(".rt")!.TextContent, row.QuerySelector(".rs")!.TextContent, Text(row.QuerySelector(".verdict")!));

    [Fact]
    public async Task One_search_says_what_is_owned_and_what_is_wanted_across_films_and_comics()
    {
        var heat = await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        var wanted = (await Api.WantMovieAsync(new() { Title = "Heat 2", Year = 2027, Directors = ["Michael Mann"], Format = MovieFormatKind.BluRay }, Ct)).Movie.ShouldNotBeNull();
        await SeedComicAsync(TestComics.Issue("Heatwave", "1", 2020));
        await SeedComicAsync(TestComics.Issue("Heatwave", "2", 2020));
        var wantedComic = (await Api.WantComicAsync(new() { Series = "Heatwave", SeriesYear = 2020, Number = "3" }, Ct)).Comic.ShouldNotBeNull();
        Navigation.NavigateTo("?q=heat");

        var page = Render<MovieLibrary>();

        page.WaitForElement(".result");
        page.FindAll(".rgroup").Select(g => g.TextContent).ShouldBe(["Movies", "Comics"]);
        page.FindAll(".result").Select(Row).ShouldBe(
        [
            ($"movies/{heat.Id}", "Heat", "1995 · Michael Mann", "Owned 4KDVD"),
            ($"wishlist/movies/{wanted.Id}", "Heat 2", "2027 · Michael Mann", "On wishlist Blu-ray"),
            ("comics?q=heat&shelf=Issues", "Heatwave (2020)", "Series", "2 issues"),
            ($"wishlist/comics/{wantedComic.Id}", "Heatwave #3", "Single issue", "On wishlist"),
        ]);
        page.Find(".verdict.wish .fmt").ClassList.ShouldContain("want-b");
        page.FindAll(".card, .filters").ShouldBeEmpty("the answers take the place of the covers");
    }

    [Fact]
    public async Task A_series_metron_knows_says_how_much_of_it_is_owned_and_opens_its_issues()
    {
        Online.Metron.AddSeries(1234, new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8"]));
        await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo("comics?q=saga");

        var page = Render<ComicLibrary>();

        var run = Row(page.WaitForElement(".result"));
        run.Verdict.ShouldBe("1 of 8");
        run.Sub.ShouldBe("Image");

        Navigation.NavigateTo(run.Href!);

        page.WaitForAssertion(() => page.FindAll(".results").ShouldBeEmpty());
        page.FindAll(".filters .chip.on").ShouldHaveSingleItem().TextContent.ShouldStartWith("Issues");
    }

    [Theory]
    [InlineData("", "movies/new?q=blade%20runner")]
    [InlineData("comics", "comics/new?q=blade%20runner")]
    public async Task Finding_nothing_offers_to_look_it_up_with_the_words_typed(string tab, string expected)
    {
        await SeedAsync(Heat);
        await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo($"{tab}?q=blade runner");

        var page = tab == "comics" ? (IRenderedComponent<Microsoft.AspNetCore.Components.IComponent>)Render<ComicLibrary>() : Render<MovieLibrary>();

        Text(page.WaitForElement(".empty")).ShouldBe("Not on your shelf Nothing you own or want matches “blade runner”. Look it up");
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe(expected);
    }

    [Fact]
    public void The_add_screen_opened_with_words_looks_them_up_straight_away()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        Navigation.NavigateTo("movies/new?q=heat");

        var page = Render<MovieEditor>();

        page.WaitForAssertion(() => Online.Tmdb.Searches.ShouldBe([("heat", null)]));
        page.Find("input[data-search]").GetAttribute("value").ShouldBe("heat");
    }

    [Fact]
    public async Task The_wishlist_tab_keeps_its_own_search_and_the_pc_its_covers()
    {
        await SeedAsync(Heat);
        Navigation.NavigateTo("wishlist?q=heat");

        Render<WishlistPage>().FindAll(".results").ShouldBeEmpty();
    }
}
