using Bunit;
using MMDb.Contracts.Movies;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

public sealed class MovieLibraryTests : ShelfTestContext
{
    private static string[] Titles(IRenderedComponent<MovieLibrary> page) =>
        [.. page.FindAll(".card .t").Select(t => t.TextContent)];

    [Fact]
    public async Task A_film_saved_elsewhere_such_as_from_a_phone_shows_up_in_the_open_library()
    {
        await SeedAsync(Heat);
        Navigation.NavigateTo("?format=Dvd");
        var page = Render<MovieLibrary>();
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Heat"]));

        await SeedAsync(Film("Alien", 1979));
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.BluRay));

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Heat"], "the filter still applies"));
        page.Find(".section-head .sub").TextContent.ShouldBe("3 films · 4 discs");
    }

    [Fact]
    public void An_empty_collection_invites_adding_the_first_movie()
    {
        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("Your shelf is empty"));
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("movies/new");
        page.Find(".section-head .sub").TextContent.ShouldBe("0 films · 0 discs");
    }

    [Fact]
    public async Task Each_film_shows_once_with_a_badge_per_format()
    {
        var heat = await SeedAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K), new(MovieFormatKind.Dvd), new(MovieFormatKind.Digital)] });

        var page = Render<MovieLibrary>();

        var card = page.WaitForElement(".card");
        card.QuerySelector(".t")!.TextContent.ShouldBe("Heat");
        card.QuerySelector(".t")!.GetAttribute("href").ShouldBe($"movies/{heat.Id}");
        card.QuerySelector(".pt")!.TextContent.ShouldBe("Heat");
        card.QuerySelector(".pd")!.TextContent.ShouldBe("Michael Mann");
        card.QuerySelector(".m")!.TextContent.ShouldContain("1995");
        card.QuerySelectorAll(".fmt").Select(b => b.TextContent).ShouldBe(["4K", "DVD", "Digital"]);
    }

    [Fact]
    public async Task The_trailer_button_opens_youtube_outside_the_app()
    {
        await SeedAsync(Heat);

        var page = Render<MovieLibrary>();

        var trailer = page.WaitForElement(".card a.play");
        trailer.GetAttribute("href").ShouldBe("https://www.youtube.com/results?search_query=Heat%201995%20trailer");
        trailer.GetAttribute("target").ShouldBe("_blank");
    }

    [Fact]
    public async Task The_header_counts_films_and_discs()
    {
        await SeedAsync(Heat with { Formats = [new(MovieFormatKind.Uhd4K), new(MovieFormatKind.Dvd), new(MovieFormatKind.Digital)] });
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.Digital));

        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => page.Find(".section-head .sub").TextContent.ShouldBe("2 films · 2 discs"));
    }

    [Fact]
    public async Task Typing_a_search_filters_the_grid_and_remembers_it_in_the_address()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        var page = Render<MovieLibrary>();
        page.WaitForAssertion(() => page.FindAll(".card").Count.ShouldBe(2));

        page.Find("input[data-search]").Input("mann");

        RelativeLocation.ShouldBe("?q=mann");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Heat"]));
    }

    [Fact]
    public async Task A_search_in_the_address_is_applied_on_arrival()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        Navigation.NavigateTo("?q=alien");

        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien"]));
        page.Find("input[data-search]").GetAttribute("value").ShouldBe("alien");
    }

    [Fact]
    public async Task Format_chips_show_counts_and_filter()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979, MovieFormatKind.BluRay));
        var page = Render<MovieLibrary>();
        page.WaitForAssertion(() => page.FindAll(".chip").Select(c => c.TextContent)
            .ShouldBe(["All2", "4K UHD1", "Blu-ray1", "DVD1", "Digital0"]));

        page.FindAll(".chip").Single(c => c.TextContent.StartsWith("Blu-ray", StringComparison.Ordinal)).Click();

        RelativeLocation.ShouldBe("?format=BluRay");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien"]));
        page.Find(".chip.on").TextContent.ShouldStartWith("Blu-ray");
        page.Find(".chip.on").GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public async Task Genre_and_decade_options_come_from_the_collection()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979) with { Genres = ["Horror"] });
        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => page.FindAll(".select")[0].QuerySelectorAll("option").Select(o => o.TextContent)
            .ShouldBe(["Any", "Crime", "Drama", "Horror"]));
        page.FindAll(".select")[1].QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Any", "1990s", "1970s"]);
    }

    [Fact]
    public async Task Choosing_a_genre_filters_the_grid()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979) with { Genres = ["Horror"] });
        var page = Render<MovieLibrary>();
        page.WaitForAssertion(() => page.FindAll(".card").Count.ShouldBe(2));

        page.FindAll(".select select")[0].Change("Horror");

        RelativeLocation.ShouldBe("?genre=Horror");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien"]));
    }

    [Fact]
    public async Task Sorting_by_title_reorders_the_grid()
    {
        await SeedAsync(Film("Heat"));
        Clock.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(Film("Alien"));
        var page = Render<MovieLibrary>();
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Heat"]));

        Clock.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(Film("Collateral"));
        page.FindAll(".select select")[2].Change(nameof(MovieSortOrder.Title));

        RelativeLocation.ShouldBe("?sort=Title");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Collateral", "Heat"]));
    }

    [Fact]
    public async Task A_search_with_no_matches_offers_to_clear_it()
    {
        await SeedAsync(Heat);
        Navigation.NavigateTo("?q=zzz");

        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("No films match"));
        page.Find(".empty a").GetAttribute("href").ShouldBe("");
    }

    [Fact]
    public async Task Films_with_a_cover_show_it_and_the_rest_keep_the_placeholder()
    {
        var heat = await SeedAsync(Heat with { TmdbId = 949, PosterPath = "/heat.jpg" });
        await SeedAsync(Film("Home Movie"));

        var page = Render<MovieLibrary>();

        page.WaitForElement(".card");
        page.FindAll(".card .poster img").Select(i => i.GetAttribute("src")).ShouldBe([heat.CoverUrl!.AbsoluteUri]);
        page.FindAll(".card .poster:not(.has-image) .pt").Select(t => t.TextContent).ShouldBe(["Home Movie"]);
    }
}
