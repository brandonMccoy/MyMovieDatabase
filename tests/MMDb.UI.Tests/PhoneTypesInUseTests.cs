using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Testing;
using MMDb.UI.Layout;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>The phone with a type turned off in its PC's Settings: no tab, no scan mode and no way over to it.</summary>
public sealed class PhoneTypesInUseTests : PhoneTestContext
{
    private const string HeatUpc = "786936894424";

    public PhoneTypesInUseTests()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
        Online.Metron.Add(TestComics.Saga7);
    }

    private IRenderedComponent<PhoneLayout> RenderApp<TPage>(string location = "")
        where TPage : IComponent
    {
        Navigation.NavigateTo(location);
        return Render<PhoneLayout>(p => p.Add(x => x.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<TPage>(0);
            b.CloseComponent();
        })));
    }

    private static IEnumerable<(string? Href, string Name)> Bar(IRenderedComponent<PhoneLayout> app) =>
        app.FindAll(".bnav a").Select(a => (a.GetAttribute("href"), Text(a)));

    private IRenderedComponent<Scan> RenderScan()
    {
        Navigation.NavigateTo("scan");
        return Render<Scan>();
    }

    [Fact]
    public async Task Without_comics_the_bottom_bar_has_four_places()
    {
        await UseAsync(movies: true);

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".bnav");
        Bar(app).ShouldBe([("", "Movies"), ("scan", ""), ("wishlist", "Wishlist"), ("stats", "Stats")]);
        app.Find(".bnav").ClassList.ShouldContain("four");
    }

    [Fact]
    public async Task Without_movies_the_bottom_bar_starts_with_comics()
    {
        await UseAsync(comics: true);

        var app = RenderApp<ComicLibrary>("comics");

        app.WaitForElement(".bnav");
        Bar(app).ShouldBe([("comics", "Comics"), ("scan", ""), ("wishlist", "Wishlist"), ("stats", "Stats")]);
        app.FindAll(".bnav a.on").ShouldHaveSingleItem().TextContent.Trim().ShouldBe("Comics");
    }

    [Fact]
    public async Task The_bar_follows_when_the_pc_turns_a_type_off()
    {
        var app = RenderApp<MovieLibrary>();
        app.WaitForElement(".bnav");
        app.Find(".bnav").ClassList.ShouldNotContain("four");

        await UseAsync(movies: true);

        app.WaitForAssertion(() => Bar(app).Select(place => place.Name).ShouldBe(["Movies", "", "Wishlist", "Stats"]));
    }

    [Theory]
    [InlineData(ProductKind.Movie)]
    [InlineData(ProductKind.Comic)]
    public async Task Do_i_own_answers_for_the_type_of_the_library_it_was_typed_on(ProductKind kind)
    {
        var heat = await SeedAsync(Heat);
        var wanted = (await Api.WantMovieAsync(new() { Title = "Heat 2", Year = 2027, Directors = ["Michael Mann"], Format = MovieFormatKind.BluRay }, Ct)).Movie.ShouldNotBeNull();
        await SeedComicAsync(TestComics.Issue("Heatwave", "1", 2020));
        var wantedComic = (await Api.WantComicAsync(new() { Series = "Heatwave", SeriesYear = 2020, Number = "3" }, Ct)).Comic.ShouldNotBeNull();
        Navigation.NavigateTo(kind == ProductKind.Comic ? "comics?q=heat" : "?q=heat");

        var page = kind == ProductKind.Comic ? (IRenderedComponent<IComponent>)Render<ComicLibrary>() : Render<MovieLibrary>();

        page.WaitForElement(".result");
        page.FindAll(".result").Select(row => row.GetAttribute("href")).ShouldBe(kind == ProductKind.Comic
            ? ["comics?q=heat&shelf=Issues", $"wishlist/comics/{wantedComic.Id}"]
            : [$"movies/{heat.Id}", $"wishlist/movies/{wanted.Id}"]);
    }

    // ---- Scanning ----

    [Fact]
    public async Task With_one_type_in_use_the_scanner_does_not_ask_what_is_being_scanned()
    {
        RenderScan().FindAll(".type-switch button").Select(b => b.TextContent).ShouldBe(["Movie", "Comic", "Auto"]);
        await DisposeComponentsAsync();

        await UseAsync(movies: true);

        RenderScan().FindAll(".type-switch").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_comics_every_barcode_is_looked_up_as_a_film()
    {
        await UseAsync(movies: true);
        var page = RenderScan();
        Scanner.ReadAddOns.ShouldBeFalse("films have no add-on to look for");

        Scanner.Read(TestComics.Saga7.Barcode!);

        var item = Queue.Items.ShouldHaveSingleItem();
        await item.Lookup;
        item.AskedAs.ShouldBe(ProductKind.Movie);
        item.Kind.ShouldBe(ProductKind.Movie);
        page.Find(".cam-bar a[aria-label='Type it in']").GetAttribute("href").ShouldBe("movies/new");
    }

    [Fact]
    public async Task Without_movies_every_barcode_is_looked_up_as_a_comic_and_closing_goes_to_comics()
    {
        await UseAsync(comics: true);
        var page = RenderScan();
        Scanner.ReadAddOns.ShouldBeTrue();

        Scanner.Read(HeatUpc);

        var item = Queue.Items.ShouldHaveSingleItem();
        await item.Lookup;
        item.Kind.ShouldBe(ProductKind.Comic);
        page.Find(".cam-bar a[aria-label='Type it in']").GetAttribute("href").ShouldBe("comics/new");
        page.Find(".cam-bar a[aria-label='Close']").GetAttribute("href").ShouldBe("comics");
    }

    [Fact]
    public async Task Turning_the_type_back_on_brings_back_the_scan_mode_that_was_chosen()
    {
        Queue.Mode = Scanning.ScanMode.Comic;
        await UseAsync(movies: true);
        Queue.AskedAs.ShouldBe(ProductKind.Movie);

        await UseAsync(movies: true, comics: true);

        Queue.AskedAs.ShouldBe(ProductKind.Comic);
    }

    // ---- No way over to a type that is off ----

    [Fact]
    public async Task Without_comics_the_add_movie_screen_offers_no_way_over_to_them()
    {
        await UseAsync(movies: true);
        Navigation.NavigateTo("movies/new");

        var page = Render<MovieEditor>();

        page.WaitForElement(".lookup");
        Text(page.Find(".lookup-foot")).ShouldBe("Not listed? Enter manually");
    }

    [Fact]
    public async Task Without_comics_a_scan_nobody_knows_is_not_offered_as_a_comic()
    {
        await UseAsync(movies: true);
        var (item, _) = Queue.Add("036000291452");
        await item.Lookup;
        Navigation.NavigateTo($"movies/new?scan={item.Id}");

        var page = Render<MovieEditor>();

        page.WaitForElement(".lookup");
        page.FindAll("button.as-other").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_movies_the_add_comic_screen_offers_no_way_over_to_them()
    {
        await UseAsync(comics: true);
        Navigation.NavigateTo("comics/new");

        var page = Render<ComicEditor>();

        page.WaitForElement(".lookup");
        Text(page.Find(".lookup-foot")).ShouldBe("Not listed? Enter manually");
    }

    [Fact]
    public async Task Without_movies_this_phone_goes_back_to_comics()
    {
        await UseAsync(comics: true);

        var back = Render<PhoneLink>().Find(".topbar a.back");

        Text(back).ShouldBe("Comics");
        back.GetAttribute("href").ShouldBe("comics");
    }

    [Fact]
    public async Task Without_comics_stats_on_the_phone_counts_only_movies()
    {
        await SeedAsync(Heat);
        await SeedComicAsync(TestComics.Saga7);
        await UseAsync(movies: true);

        var page = Render<StatsPage>();

        page.WaitForElement(".tiles");
        page.FindAll(".tile .k").Select(k => k.TextContent).ShouldBe(["Films", "Discs", "Digital", "Wishlist"]);
    }
}
