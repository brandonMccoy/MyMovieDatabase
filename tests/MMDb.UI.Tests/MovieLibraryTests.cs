using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MMDb.Contracts.Movies;
using MMDb.UI.Pages;
using static MMDb.UI.Tests.FilterPanel;

namespace MMDb.UI.Tests;

public sealed class MovieLibraryTests : ShelfTestContext
{
    private static string[] Titles(IRenderedComponent<MovieLibrary> page) =>
        [.. page.FindAll(".card .t").Select(t => t.TextContent)];

    /// <summary>Heat (1995, crime drama, 4K and DVD), Alien (1979, horror, Blu-ray) and Arrival (2016, drama, digital), with the library open.</summary>
    private async Task<IRenderedComponent<MovieLibrary>> LibraryOfThreeAsync(string address = "")
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979, MovieFormatKind.BluRay) with { Genres = ["Horror"] });
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.Digital) with { Genres = ["Drama"] });
        Navigation.NavigateTo(address);
        var page = Render<MovieLibrary>();
        page.WaitForElement(".filters");
        return page;
    }

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
    public async Task A_card_links_to_the_film_and_nowhere_else()
    {
        var heat = await SeedAsync(Heat);

        var page = Render<MovieLibrary>();

        var card = page.WaitForElement(".card");
        card.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).Distinct().ShouldBe([$"movies/{heat.Id}"]);
        card.TextContent.ShouldNotContain("Trailer");
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
    public async Task A_director_in_the_address_lists_that_directors_films_until_the_chip_is_cleared()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979) with { Directors = ["Ridley Scott"] });
        Navigation.NavigateTo("movies?director=Michael%20Mann");

        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Heat"]));
        var chip = page.Find(".chip.director");
        chip.TextContent.ShouldBe("Directed by Michael Mann");
        Chips(page).ShouldBe(["Filter", "Directed by Michael Mann"]);

        Navigation.NavigateTo(chip.GetAttribute("href")!);

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Heat"], ignoreOrder: true));
        page.FindAll(".chip.director").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_actor_in_the_address_lists_that_actors_films_until_the_chip_is_cleared()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979) with { Cast = [new("Sigourney Weaver", "Ripley")] });
        Navigation.NavigateTo("movies?actor=Al%20Pacino");

        var page = Render<MovieLibrary>();

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Heat"]));
        var chip = page.Find(".chip.actor");
        chip.TextContent.ShouldBe("With Al Pacino");
        chip.GetAttribute("aria-label").ShouldBe("With Al Pacino: clear");
        Chips(page).ShouldBe(["Filter", "With Al Pacino"]);

        Navigation.NavigateTo(chip.GetAttribute("href")!);

        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Heat"], ignoreOrder: true));
        page.FindAll(".chip.actor").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_filter_button_drops_a_panel_of_tick_boxes_with_a_count_per_format()
    {
        var page = await LibraryOfThreeAsync();
        Chips(page).ShouldBe(["Filter"]);
        page.FindAll(".filters select").Count.ShouldBe(1, "only the sort is a dropdown");
        page.FindAll("[role=dialog]").ShouldBeEmpty();

        page.Find(".open-filters").Click();

        var panel = page.Find(".filter-pop");
        panel.GetAttribute("role").ShouldBe("dialog");
        panel.GetAttribute("aria-label").ShouldBe("Filters");
        Boxes(page).ShouldBe([("Format", "4K UHD 1, Blu-ray 1, DVD 1, Digital 1"), ("Genre", "Crime, Drama, Horror"), ("Decade", "2010s, 1990s, 1970s")]);
        Ticked(page).ShouldBeEmpty();
        panel.QuerySelectorAll(".sheet-foot button").Select(b => b.TextContent).ShouldBe(["Clear", "Apply Filter"]);
        page.Find(".open-filters").GetAttribute("aria-expanded").ShouldBe("true");
        page.FindAll(".sheet, .sheet-head, .sheet-shade").ShouldBeEmpty("the sheet is the phone's");
    }

    [Fact]
    public async Task Ticks_filter_nothing_until_apply_filter_and_then_show_as_chips()
    {
        var page = await LibraryOfThreeAsync();
        page.Find(".open-filters").Click();

        Tick(page, "Blu-ray");
        Tick(page, "Digital");

        RelativeLocation.ShouldBe("");
        Titles(page).Length.ShouldBe(3);
        Chips(page).ShouldBe(["Filter"]);

        Apply(page);

        RelativeLocation.ShouldBe("?format=BluRay&format=Digital");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Arrival"], ignoreOrder: true));
        page.FindAll("[role=dialog], .pop-shade").ShouldBeEmpty();
        Chips(page).ShouldBe(["Filter", "Blu-ray", "Digital"]);
        page.Find(".section-head .sub").TextContent.ShouldBe("3 films · 3 discs", "the header counts the whole collection");
    }

    [Fact]
    public async Task Ticks_in_one_filter_widen_it_and_the_other_filters_narrow_it()
    {
        var page = await LibraryOfThreeAsync();
        page.Find(".open-filters").Click();

        Tick(page, "2010s");
        Tick(page, "Drama");
        Tick(page, "Digital");
        Tick(page, "DVD");
        Apply(page);

        RelativeLocation.ShouldBe("?format=Dvd&format=Digital&genre=Drama&decade=2010");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Arrival"]));
        Chips(page).ShouldBe(["Filter", "DVD", "Digital", "Drama", "2010s"]);
    }

    [Fact]
    public async Task The_format_counts_follow_the_genres_and_decades_applied_but_not_the_formats()
    {
        var page = await LibraryOfThreeAsync("?genre=Drama&format=Digital");

        page.Find(".open-filters").Click();

        Boxes(page)[0].ShouldBe(("Format", "4K UHD 1, Blu-ray 0, DVD 1, Digital 1"));
        Ticked(page).ShouldBe(["Digital", "Drama"]);
    }

    [Fact]
    public async Task Esc_or_a_click_outside_closes_the_panel_and_changes_nothing()
    {
        var page = await LibraryOfThreeAsync("?format=Dvd&decade=1990");
        page.Find(".open-filters").Click();
        Ticked(page).ShouldBe(["DVD", "1990s"]);

        Tick(page, "Blu-ray");
        Tick(page, "DVD", on: false);
        page.Find(".filter-pop").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.FindAll("[role=dialog]").ShouldBeEmpty();
        RelativeLocation.ShouldBe("?format=Dvd&decade=1990");
        Titles(page).ShouldBe(["Heat"]);

        page.Find(".open-filters").Click();
        Ticked(page).ShouldBe(["DVD", "1990s"], "the ticks that weren't applied are forgotten");

        page.Find(".pop-shade").Click();

        page.FindAll("[role=dialog], .pop-shade").ShouldBeEmpty();
        RelativeLocation.ShouldBe("?format=Dvd&decade=1990");
    }

    [Fact]
    public async Task A_chip_takes_its_own_filter_off()
    {
        var page = await LibraryOfThreeAsync("?format=BluRay&format=Dvd&genre=Horror&decade=1970&sort=Title");
        Chips(page).ShouldBe(["Filter", "Blu-ray", "DVD", "Horror", "1970s"]);
        Titles(page).ShouldBe(["Alien"]);

        var dvd = page.FindAll(".filters a.chip").Single(c => c.TextContent == "DVD");
        dvd.GetAttribute("aria-label").ShouldBe("DVD: clear");
        Navigation.NavigateTo(dvd.GetAttribute("href")!);

        RelativeLocation.ShouldBe("?format=BluRay&genre=Horror&decade=1970&sort=Title");
        page.WaitForAssertion(() => Chips(page).ShouldBe(["Filter", "Blu-ray", "Horror", "1970s"]));
    }

    [Fact]
    public async Task Clear_in_the_panel_takes_every_filter_off_at_once_and_keeps_the_search_and_sort()
    {
        var page = await LibraryOfThreeAsync("?q=a&format=Dvd&genre=Crime&decade=1990&director=Michael%20Mann&sort=Title");
        Chips(page).ShouldBe(["Filter", "DVD", "Crime", "1990s", "Directed by Michael Mann"]);
        page.Find(".open-filters").Click();

        Clear(page);

        RelativeLocation.ShouldBe("?q=a&sort=Title");
        page.FindAll("[role=dialog]").ShouldBeEmpty();
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Arrival", "Heat"]));
        Chips(page).ShouldBe(["Filter"]);
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
        page.Find(".select select").Change(nameof(MovieSortOrder.Title));

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

    // With a thousand films, covers marked decoding="async" loaded but were never drawn: the WebView only decodes
    // so many of those at a time, and a wide window of w500 posters is more than that. Seen on the PC, 2026-10-10.
    [Fact]
    public async Task Covers_are_decoded_with_the_page_so_none_is_left_undrawn()
    {
        await SeedAsync(Heat with { TmdbId = 949, PosterPath = "/heat.jpg" });

        var page = Render<MovieLibrary>();

        page.WaitForElement(".card");
        var cover = page.Find(".card .poster img");
        cover.GetAttribute("decoding").ShouldBe("sync");
        cover.GetAttribute("loading").ShouldBe("lazy", "covers far down the list still wait until they are scrolled near");
    }
}
