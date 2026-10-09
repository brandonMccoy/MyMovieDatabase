using Bunit;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

public sealed class HomeTests : BunitContext
{
    [Fact]
    public void Home_shows_the_app_name()
    {
        var home = Render<Home>();

        home.Find("h1").TextContent.ShouldBe("Shelf");
    }
}
