using AngleSharp.Dom;
using Bunit;
using MMDb.Application.Metadata;
using MMDb.Contracts.Movies;
using MMDb.Testing;
using MMDb.UI.Pages;
using MMDb.UI.Stats;

namespace MMDb.UI.Tests;

/// <summary>The Stats screen on the PC: the tiles, the five charts, and where their bars lead. The clock starts on 15 January 2026.</summary>
public sealed class StatsScreenTests : ShelfTestContext
{
    private static SaveMovieRequest By(string director, string title, int year, params MovieFormatKind[] formats) =>
        Film(title, year, formats) with { Directors = [director] };

    private static string Tile(IRenderedComponent<StatsPage> page, string name) =>
        string.Join(" | ", page.FindAll(".tile").Single(t => t.QuerySelector(".k")!.TextContent == name).Children.Skip(1).Select(c => c.TextContent.Trim()));

    private static IElement Chart(IRenderedComponent<StatsPage> page, string title) =>
        page.FindAll(".chart").Single(c => c.QuerySelector("h4")!.TextContent == title);

    private async Task<IRenderedComponent<StatsPage>> RenderStatsAsync()
    {
        var page = Render<StatsPage>();
        await page.InvokeAsync(() => { });
        page.WaitForElement(".tiles, .empty");
        return page;
    }

    [Fact]
    public async Task An_empty_collection_says_so_instead_of_showing_empty_charts()
    {
        var page = await RenderStatsAsync();

        page.Find(".empty h2").TextContent.ShouldBe("Nothing to count yet");
        page.FindAll(".chart").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tiles_give_the_headline_numbers()
    {
        await SeedAsync(Heat); // 4K UHD and DVD
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.Digital));
        await SeedComicAsync(TestComics.Saga7);
        await SeedComicAsync(TestComics.Daredevil181);
        await SeedComicAsync(TestComics.SagaVolume1);
        await Api.WantMovieAsync(new() { Title = "Dune", Year = 2021, Format = MovieFormatKind.Uhd4K }, Ct);

        var page = await RenderStatsAsync();

        page.FindAll(".tile .k").Select(k => k.TextContent).ShouldBe(["Films", "Discs", "Digital", "Comic issues", "Series", "Wishlist"]);
        Tile(page, "Films").ShouldBe("2 | +2 this month");
        Tile(page, "Discs").ShouldBe("2 | 1 film on 2+ formats");
        Tile(page, "Digital").ShouldBe("1");
        Tile(page, "Comic issues").ShouldBe("2 | +2 this month | 1 trade or HC");
        Tile(page, "Series").ShouldBe("2 | 0 complete runs");
        Tile(page, "Wishlist").ShouldBe("1 | 1 movie · 0 comics");
    }

    [Fact]
    public async Task What_was_added_in_earlier_months_is_not_this_months()
    {
        await SeedAsync(Heat);
        Clock.SetUtcNow(new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero));

        var page = await RenderStatsAsync();

        Tile(page, "Films").ShouldBe("1 | None this month");
    }

    [Fact]
    public async Task The_digital_tile_says_how_much_room_the_films_files_take()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        await SeedAsync(Film("Alien", 1979));
        File.WriteAllBytes(Path.Combine(MoviesFolder, "Heat.1995.1080p.mkv"), new byte[3 * 1024 * 1024 / 2]);
        (await Digital.SetFolderAsync(MoviesFolder, Ct)).ShouldBeTrue();
        await Digital.MatchAsync(Ct);
        var page = await RenderStatsAsync();
        Tile(page, "Digital").ShouldBe("0");

        (await Digital.AddTickedAsync(Ct)).ShouldBe(1);

        page.WaitForAssertion(() => Tile(page, "Digital").ShouldBe("1 | 1.5 MB on this PC"));
    }

    [Fact]
    public async Task Copies_by_format_draws_a_part_per_format_owned_and_each_opens_that_format()
    {
        await SeedAsync(Heat); // 4K UHD and DVD
        await SeedAsync(Film("Alien", 1979, MovieFormatKind.Dvd));
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.Dvd, MovieFormatKind.Digital));

        var chart = Chart(await RenderStatsAsync(), "Copies by format");

        chart.QuerySelector(".cap")!.TextContent.ShouldBe("5 copies · 4 discs + 1 digital");
        chart.QuerySelectorAll(".stack .seg").Select(s => (s.GetAttribute("href"), s.GetAttribute("data-tip"), s.GetAttribute("style"))).ShouldBe(
        [
            ("movies?format=Uhd4K", "4K UHD · 1 disc (20%)", "flex-grow:1"),
            ("movies?format=Dvd", "DVD · 3 discs (60%)", "flex-grow:3"),
            ("movies?format=Digital", "Digital · 1 copy (20%)", "flex-grow:1"),
        ]);
        chart.QuerySelectorAll(".legend a").Select(a => a.TextContent.Trim()).ShouldBe(["4K UHD 1", "Blu-ray 0", "DVD 3", "Digital 1"]);
    }

    [Fact]
    public async Task Items_added_per_month_covers_the_last_twelve_months_ending_with_this_one()
    {
        await SeedAsync(Heat); // two formats, in January
        Clock.SetUtcNow(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));
        await SeedComicAsync(TestComics.Saga7);

        var chart = Chart(await RenderStatsAsync(), "Items added per month");

        chart.QuerySelectorAll(".xlabels span").Select(s => s.TextContent)
            .ShouldBe(["Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", "Jan", "Feb", "Mar"]);
        var bars = chart.QuerySelectorAll(".col i");
        bars.Select(b => b.GetAttribute("data-tip")).TakeLast(3)
            .ShouldBe(["Jan 2026 · 2 items added", "Feb 2026 · nothing added", "Mar 2026 · 1 item added"]);
        bars.Select(b => b.GetAttribute("style")).TakeLast(3).ShouldBe(["height:100%", "height:0%", "height:50%"]);
        chart.QuerySelectorAll("a").ShouldBeEmpty("a month has no list to open");
    }

    [Fact]
    public async Task Films_by_decade_opens_the_library_on_a_decade()
    {
        await SeedAsync(Film("Alien", 1979));
        await SeedAsync(Film("Heat", 1995));
        await SeedAsync(Film("Fargo", 1996));
        await SeedAsync(Film("Home video"));

        var chart = Chart(await RenderStatsAsync(), "Films by decade");

        chart.QuerySelector(".cap")!.TextContent.ShouldBe("By year of release · 1 film without a year");
        chart.QuerySelectorAll(".xlabels span").Select(s => s.TextContent).ShouldBe(["1970s", "1980s", "1990s"]);
        chart.QuerySelectorAll(".col").Select(c => (c.GetAttribute("href"), c.GetAttribute("aria-label"))).ShouldBe(
        [
            ("movies?decade=1970", "1970s · 1 film"),
            (null, "1980s · 0 films"),
            ("movies?decade=1990", "1990s · 2 films"),
        ]);
    }

    [Fact]
    public async Task A_director_bar_opens_exactly_the_films_it_counts()
    {
        await SeedAsync(By("Michael Mann", "Heat", 1995));
        await SeedAsync(By("Michael Mann", "Collateral", 2004));
        await SeedAsync(By("Ridley Scott", "Alien", 1979));
        await SeedAsync(By("Peter Berg", "Hancock", 2008) with { Crew = [new("Michael Mann", "Producer", "Production")] });

        var chart = Chart(await RenderStatsAsync(), "Top directors");

        chart.QuerySelectorAll(".hbar").Select(b => (b.QuerySelector(".lbl")!.TextContent, b.QuerySelector(".num")!.TextContent))
            .ShouldBe([("Michael Mann", "2"), ("Peter Berg", "1"), ("Ridley Scott", "1")]);
        var mann = chart.QuerySelector("a.hbar")!;
        mann.GetAttribute("href").ShouldBe("movies?director=Michael%20Mann");
        mann.QuerySelector(".track i")!.GetAttribute("style").ShouldBe("width:100%");

        Navigation.NavigateTo(mann.GetAttribute("href")!);
        var library = Render<MovieLibrary>();
        library.WaitForAssertion(() => library.FindAll(".card .t").Select(t => t.TextContent).ShouldBe(["Collateral", "Heat"], ignoreOrder: true));
    }

    [Fact]
    public async Task A_publisher_bar_opens_that_publishers_issues()
    {
        await SeedComicAsync(TestComics.Saga7); // Image
        await SeedComicAsync(TestComics.SagaIssue("8"));
        await SeedComicAsync(TestComics.Daredevil181); // Marvel
        await SeedComicAsync(TestComics.SagaVolume1); // a trade: not an issue

        var chart = Chart(await RenderStatsAsync(), "Comics by publisher");

        chart.QuerySelectorAll(".hbar").Select(b => (b.GetAttribute("href"), b.GetAttribute("data-tip")))
            .ShouldBe([("comics?shelf=Issues&publisher=Image", "Image · 2 issues"), ("comics?shelf=Issues&publisher=Marvel", "Marvel · 1 issue")]);

        Navigation.NavigateTo("comics?shelf=Issues&publisher=Image");
        var library = Render<ComicLibrary>();
        library.WaitForAssertion(() => library.FindAll(".issue").Count.ShouldBe(2));
    }

    [Fact]
    public async Task Charts_without_anything_to_draw_say_so()
    {
        await SeedComicAsync(TestComics.SagaVolume1);

        var page = await RenderStatsAsync();

        Chart(page, "Copies by format").QuerySelector(".none")!.TextContent.ShouldBe("No films yet.");
        Chart(page, "Films by decade").QuerySelector(".none").ShouldNotBeNull();
        Chart(page, "Top directors").QuerySelector(".none").ShouldNotBeNull();
        Chart(page, "Comics by publisher").QuerySelector(".none")!.TextContent.ShouldBe("No comic issues yet.");
    }

    [Fact]
    public async Task The_numbers_follow_what_is_saved_elsewhere_such_as_from_a_phone()
    {
        await SeedAsync(Heat);
        var page = await RenderStatsAsync();

        await SeedAsync(Film("Alien", 1979, MovieFormatKind.BluRay));

        page.WaitForAssertion(() => Tile(page, "Films").ShouldBe("2 | +2 this month"));
        Chart(page, "Copies by format").QuerySelectorAll(".seg").Length.ShouldBe(3);
    }
}

public sealed class ChartAxisTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 12)]
    [InlineData(31, 40)]
    [InlineData(100, 100)]
    [InlineData(101, 120)]
    [InlineData(1173, 1200)]
    [InlineData(1201, 1600)]
    [InlineData(9001, 10000)]
    public void The_top_of_a_chart_is_a_round_even_number_at_or_above_the_tallest_bar(int tallest, int top)
    {
        ChartAxis.Top(tallest).ShouldBe(top);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(900, "900 bytes")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 * 1024, "10 MB")]
    [InlineData(229_780_750_336, "214 GB")]
    [InlineData(2_089_072_092_774, "1.9 TB")]
    [InlineData(5_000_000_000_000_000, "4,547 TB")]
    public void File_sizes_read_as_windows_shows_them(long bytes, string text)
    {
        FileSize.Text(bytes).ShouldBe(text);
    }
}
