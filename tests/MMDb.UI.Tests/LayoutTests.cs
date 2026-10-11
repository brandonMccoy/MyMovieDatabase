using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MMDb.UI.Components;
using MMDb.UI.Layout;

namespace MMDb.UI.Tests;

public sealed class LayoutTests : ShelfTestContext
{
    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

    [Fact]
    public void The_sidebar_shows_the_app_name()
    {
        RenderLayout().Find(".brand-name").TextContent.ShouldBe("Shelf");
    }

    [Fact]
    public async Task The_sidebar_counts_movies_and_updates_when_told()
    {
        await SeedAsync(Heat);
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.Find(".nav .count").TextContent.ShouldBe("1"));

        await SeedAsync(Film("Alien"));
        await layout.InvokeAsync(() => Services.GetRequiredService<ShelfCounts>().RefreshAsync(Ct));

        layout.WaitForAssertion(() => layout.Find(".nav .count").TextContent.ShouldBe("2"));
    }

    [Fact]
    public async Task The_sidebar_counts_follow_what_is_saved_elsewhere_such_as_from_a_phone()
    {
        var layout = RenderLayout();
        layout.WaitForAssertion(() => layout.FindAll(".nav .count").Select(c => c.TextContent).ShouldBe(["0", "0", "0"]));

        await SeedAsync(Heat);
        await SeedComicAsync(MMDb.Testing.TestComics.Saga7);
        await Api.WantMovieAsync(new() { Title = "Dune", Year = 2021, Format = Contracts.Movies.MovieFormatKind.Uhd4K }, Ct);

        layout.WaitForAssertion(() => layout.FindAll(".nav .count").Select(c => c.TextContent).ShouldBe(["1", "1", "1"]));
    }

    [Theory]
    [InlineData("", "Movies0")]
    [InlineData("?format=Dvd", "Movies0")]
    [InlineData("movies/new", "Movies0")]
    [InlineData("import", "Movies0")]
    [InlineData("comics?shelf=Issues", "Comics0")]
    [InlineData("wishlist", "Wishlist0")]
    [InlineData("wishlist/movies/new", "Wishlist0")]
    [InlineData("stats", "Stats")]
    [InlineData("settings", "Settings")]
    public void The_sidebar_lights_up_the_screen_you_are_on_and_no_other(string address, string lit)
    {
        Navigation.NavigateTo(address);

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.FindAll(".nav a.active").Select(a => a.TextContent.Trim()).ShouldBe([lit]));
    }

    [Fact]
    public void Only_built_screens_appear_in_the_sidebar()
    {
        RenderLayout().FindAll(".nav a").Select(a => a.TextContent.Trim()).ShouldBe(["Movies0", "Comics0", "Wishlist0", "Stats", "Settings"]);
    }
}

public sealed class PosterColorsTests
{
    [Fact]
    public void The_same_title_always_gets_the_same_colors()
    {
        PosterColors.For("Heat").ShouldBe(PosterColors.For(" heat "));
    }

    [Fact]
    public void Titles_spread_across_the_palette()
    {
        string[] titles = ["Heat", "Alien", "Arrival", "Jaws", "Collateral", "Sicario", "Prisoners", "Oppenheimer", "Parasite", "The Thing"];

        titles.Select(PosterColors.For).Distinct().Count().ShouldBeGreaterThan(5);
    }
}
