using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MMDb.Application.Types;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Types;
using MMDb.Local;
using MMDb.Testing;
using MMDb.UI.Layout;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Settings → Collections on the PC: choosing the types Shelf shows, and what a type that is off leaves behind (nothing on screen, everything in the database).</summary>
public sealed class TypesInUseTests : ShelfTestContext
{
    private static string Text(IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private static IElement Switch(IRenderedComponent<Settings> page, ProductKind kind) => page.Find($".use-type[data-type='{kind}']");

    /// <summary>The Collections rows as read: "Movies | on", "Comics | off | Shelf shows at least one type".</summary>
    private static IEnumerable<string> Rows(IRenderedComponent<Settings> page) =>
        page.FindAll("section[aria-labelledby='types-heading'] .setrow").Select(row => string.Join(" | ", new[]
        {
            Text(row.QuerySelector(".lbl")!),
            row.QuerySelector(".use-type")!.GetAttribute("aria-checked") == "true" ? "on" : "off",
            row.QuerySelector(".use-type")!.HasAttribute("disabled") ? "fixed" : null,
            Text(row.QuerySelector(".small")!) is { Length: > 0 } note ? note : null,
        }.OfType<string>()));

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

    private static IEnumerable<string> Sidebar(IRenderedComponent<MainLayout> layout) => layout.FindAll(".nav a").Select(a => a.TextContent.Trim());

    private async Task<IRenderedComponent<StatsPage>> RenderStatsAsync()
    {
        var page = Render<StatsPage>();
        await page.InvokeAsync(() => { });
        page.WaitForElement(".tiles, .empty");
        return page;
    }

    private async Task OneOfEachAsync()
    {
        await SeedAsync(Heat);
        await SeedComicAsync(TestComics.Saga7);
        await Api.WantMovieAsync(new() { Title = "Dune", Year = 2021, Format = MovieFormatKind.Uhd4K }, Ct);
        await Api.WantComicAsync(new() { Series = "Saga", SeriesYear = 2012, Number = "8" }, Ct);
    }

    // ---- Settings ----

    [Fact]
    public void Collections_comes_first_in_settings_with_every_type_on()
    {
        var page = Render<Settings>();

        page.FindAll(".settings > section h3")[0].TextContent.ShouldBe("Collections");
        Rows(page).ShouldBe(["Movies | on", "Comics | on"]);
        Switch(page, ProductKind.Comic).GetAttribute("aria-label").ShouldBe("Show comics");
    }

    [Fact]
    public async Task Turning_a_type_off_keeps_what_was_saved_and_the_last_type_cannot_be_turned_off()
    {
        await OneOfEachAsync();
        var page = Render<Settings>();

        Switch(page, ProductKind.Comic).Click();

        page.WaitForAssertion(() => Rows(page).ShouldBe(["Movies | on | fixed | Shelf shows at least one type", "Comics | off"]));
        (await Api.ListComicsAsync(new(), Ct)).Facets.IssueCount.ShouldBe(1);
        (await Api.ListWishlistAsync(cancellationToken: Ct)).ComicCount.ShouldBe(1);
    }

    [Fact]
    public async Task Turning_a_type_back_on_brings_back_everything_saved_for_it()
    {
        await OneOfEachAsync();
        await UseAsync(movies: true);
        var layout = RenderLayout();
        var page = Render<Settings>();
        layout.WaitForAssertion(() => Sidebar(layout).ShouldBe(["Movies1", "Wishlist1", "Stats", "Settings"]));

        Switch(page, ProductKind.Comic).Click();

        page.WaitForAssertion(() => Rows(page).ShouldBe(["Movies | on", "Comics | on"]));
        layout.WaitForAssertion(() => Sidebar(layout).ShouldBe(["Movies1", "Comics1", "Wishlist2", "Stats", "Settings"]));
    }

    [Fact]
    public async Task The_choice_is_still_there_the_next_time_shelf_starts()
    {
        var page = Render<Settings>();
        Switch(page, ProductKind.Movie).Click();
        page.WaitForAssertion(() => Types.InUse.ShouldBe(new TypesInUseDto(Movies: false, Comics: true)));

        var restarted = new InProcessTypesApi(Services.GetRequiredService<TypeChoice>());
        restarted.InUse.ShouldBe(new TypesInUseDto(), "nothing is read before the app asks");
        await restarted.LoadAsync(Ct);

        restarted.InUse.ShouldBe(new TypesInUseDto(Movies: false, Comics: true));
    }

    [Fact]
    public async Task A_choice_of_no_type_at_all_changes_nothing()
    {
        await UseAsync(comics: true);

        (await Types.SetAsync(new(Movies: false, Comics: false), Ct)).ShouldBe(new TypesInUseDto(Movies: false, Comics: true));
    }

    [Fact]
    public async Task Without_movies_settings_has_no_movie_folder_and_asks_for_no_movie_keys()
    {
        await UseAsync(comics: true);

        var page = Render<Settings>();

        page.FindAll(".settings > section h3").Select(h => h.TextContent).ShouldBe(["Collections", "Phone", "Obsidian vault", "Online databases"]);
        page.FindAll("section[aria-labelledby='online-heading'] .setrow .lbl").Select(Text).ShouldBe(["Metron · comics"]);
    }

    [Fact]
    public async Task Without_comics_settings_asks_for_no_comic_token()
    {
        await UseAsync(movies: true);

        var page = Render<Settings>();

        page.FindAll(".settings > section h3").Select(h => h.TextContent).ShouldBe(["Collections", "Phone", "Obsidian vault", "Digital movies", "Online databases"]);
        page.FindAll("section[aria-labelledby='online-heading'] .setrow .lbl").Select(Text).ShouldBe(["TMDB · movies, trailers", "UPCitemdb · movie barcodes"]);
    }

    // ---- Sidebar and search ----

    [Fact]
    public async Task The_sidebar_leaves_out_a_type_that_is_off_and_counts_the_wishlist_without_it()
    {
        await OneOfEachAsync();
        await UseAsync(comics: true);

        var layout = RenderLayout();

        layout.WaitForAssertion(() => Sidebar(layout).ShouldBe(["Comics1", "Wishlist1", "Stats", "Settings"]));
    }

    [Fact]
    public async Task Without_movies_the_sidebar_says_nothing_of_new_video_files()
    {
        File.WriteAllText(Path.Combine(MoviesFolder, "Heat.1995.1080p.mkv"), "");
        (await Digital.SetFolderAsync(MoviesFolder, Ct)).ShouldBeTrue();
        var layout = RenderLayout();
        layout.WaitForElement(".new-files", TimeSpan.FromSeconds(10));

        await UseAsync(comics: true);

        layout.WaitForAssertion(() => layout.FindAll(".new-files").ShouldBeEmpty());
    }

    [Fact]
    public void Without_movies_the_search_box_of_a_screen_that_is_no_library_searches_comics()
    {
        var page = Render<Settings>();
        page.Find(".topbar input[data-search]").GetAttribute("aria-label").ShouldBe("Search movies");

        Switch(page, ProductKind.Movie).Click();

        page.WaitForAssertion(() => page.Find(".topbar input[data-search]").GetAttribute("aria-label").ShouldBe("Search comics"));
        var box = page.Find(".topbar input[data-search]");
        box.GetAttribute("placeholder").ShouldBe("Search series, issues, creators…");

        box.Input("saga");
        box.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        RelativeLocation.ShouldBe("comics?q=saga");
    }

    // ---- Wishlist ----

    [Theory]
    [InlineData(ProductKind.Movie, "?tab=comics", "Dune", "wishlist/movies/new")]
    [InlineData(ProductKind.Comic, "", "Saga #8", "wishlist/comics/new")]
    public async Task With_one_type_in_use_the_wishlist_is_that_types_list_without_tabs(ProductKind only, string query, string wanted, string addsAt)
    {
        await OneOfEachAsync();
        await UseAsync(movies: only == ProductKind.Movie, comics: only == ProductKind.Comic);
        Navigation.NavigateTo("wishlist" + query);

        var page = Render<WishlistPage>();

        page.WaitForAssertion(() => page.FindAll(".wl .wl-title a").Select(Text).ShouldBe([wanted]));
        page.FindAll(".subtabs").ShouldBeEmpty();
        page.Find(".section-head .sub").TextContent.ShouldBe("1 item");
        page.Find(".topbar a.btn.primary").GetAttribute("href").ShouldBe(addsAt);
    }

    // ---- Stats ----

    [Fact]
    public async Task Without_comics_stats_counts_and_charts_only_movies()
    {
        await OneOfEachAsync();
        await UseAsync(movies: true);

        var page = await RenderStatsAsync();

        page.FindAll(".tile .k").Select(k => k.TextContent).ShouldBe(["Films", "Discs", "Digital", "Wishlist"]);
        page.FindAll(".tile")[^1].QuerySelectorAll(".v, .d").Select(Text).ShouldBe(["1"], "one type needs no split by type");
        page.FindAll(".chart h4").Select(h => h.TextContent).ShouldBe(["Copies by format", "Movies added per month", "Films by decade", "Top directors", "Top actors"]);
    }

    [Fact]
    public async Task Without_movies_stats_counts_and_charts_only_comics()
    {
        await OneOfEachAsync();
        await UseAsync(comics: true);

        var page = await RenderStatsAsync();

        page.FindAll(".tile .k").Select(k => k.TextContent).ShouldBe(["Comic issues", "Series", "Wishlist"]);
        page.FindAll(".tile")[^1].QuerySelectorAll(".v, .d").Select(Text).ShouldBe(["1"], "one type needs no split by type");
        page.FindAll(".chart h4").Select(h => h.TextContent).ShouldBe(["Comics added per month", "Comics by publisher"]);
    }

    [Fact]
    public async Task Stats_with_only_a_type_that_is_off_to_count_has_nothing_to_count()
    {
        await SeedAsync(Heat);
        await UseAsync(comics: true);

        var page = await RenderStatsAsync();

        page.Find(".empty p").TextContent.ShouldBe("The numbers appear once there is a comic in your collection.");
        Text(page.Find(".empty a.btn")).ShouldBe("Add comic");
        page.Find(".empty a.btn").GetAttribute("href").ShouldBe("comics/new");
    }

    // ---- Where a type's screens go ----

    [Theory]
    [InlineData("", ProductKind.Movie)]
    [InlineData("?format=Dvd", ProductKind.Movie)]
    [InlineData("movies", ProductKind.Movie)]
    [InlineData("movies/new?q=heat", ProductKind.Movie)]
    [InlineData("import", ProductKind.Movie)]
    [InlineData("wishlist/movies/new", ProductKind.Movie)]
    [InlineData("comics", ProductKind.Comic)]
    [InlineData("comics/new", ProductKind.Comic)]
    [InlineData("comics?shelf=Issues", ProductKind.Comic)]
    [InlineData("wishlist/comics/3f2c", ProductKind.Comic)]
    [InlineData("wishlist", null)]
    [InlineData("wishlist?tab=comics", null)]
    [InlineData("stats", null)]
    [InlineData("scan/review", null)]
    [InlineData("settings", null)]
    [InlineData("comicsandmore", null)]
    public void Every_screen_belongs_to_a_type_or_to_none(string address, ProductKind? kind)
    {
        TypeRoutes.KindOf(address).ShouldBe(kind);
    }

    [Theory]
    [InlineData("", true, false, null)]
    [InlineData("comics/new", true, true, null)]
    [InlineData("comics/new", true, false, "")]
    [InlineData("wishlist/comics/new", true, false, "")]
    [InlineData("", false, true, "comics")]
    [InlineData("movies/new?q=heat", false, true, "comics")]
    [InlineData("import", false, true, "comics")]
    [InlineData("wishlist", false, true, null)]
    [InlineData("settings", false, true, null)]
    public void A_screen_of_a_type_that_is_off_gives_way_to_the_first_type_in_use(string address, bool movies, bool comics, string? instead)
    {
        TypeRoutes.Instead(address, new(movies, comics)).ShouldBe(instead);
    }

    [Fact]
    public async Task Opening_shelf_without_movies_lands_on_comics()
    {
        await SeedComicAsync(TestComics.Saga7);
        await UseAsync(comics: true);

        var app = Render<Routes>();

        app.WaitForAssertion(() => RelativeLocation.ShouldBe("comics"));
        app.WaitForAssertion(() => app.Find(".section-head h1").TextContent.ShouldBe("Comics"));
    }

    [Fact]
    public async Task A_screen_left_open_gives_way_when_its_type_is_turned_off()
    {
        Navigation.NavigateTo("comics");
        var app = Render<Routes>();
        app.WaitForAssertion(() => app.Find(".section-head h1").TextContent.ShouldBe("Comics"));

        await UseAsync(movies: true);

        app.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        app.WaitForAssertion(() => app.Find(".section-head h1").TextContent.ShouldBe("Movies"));
    }
}
