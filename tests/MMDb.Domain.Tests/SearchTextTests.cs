using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("heat 1995", "heat", 1995)]
    [InlineData("Heat (1995)", "Heat", 1995)]
    [InlineData("Heat, 1995", "Heat", 1995)]
    [InlineData("  the   thing  1982 ", "the thing", 1982)]
    [InlineData("Blade Runner 2049", "Blade Runner", 2049)]
    public void A_trailing_year_is_split_from_the_title(string input, string title, int year)
    {
        var text = SearchText.Parse(input);

        text.Title.ShouldBe(title);
        text.Year.ShouldBe(year);
    }

    [Theory]
    [InlineData("Heat", "Heat")]
    [InlineData("1917", "1917")]
    [InlineData("2001: A Space Odyssey", "2001: A Space Odyssey")]
    [InlineData("Heat 3000", "Heat 3000")]
    [InlineData("Ocean's 11", "Ocean's 11")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Without_a_trailing_year_the_whole_text_is_the_title(string? input, string title)
    {
        var text = SearchText.Parse(input);

        text.Title.ShouldBe(title);
        text.Year.ShouldBeNull();
    }

    [Fact]
    public void The_full_text_is_kept_for_titles_that_end_in_a_year()
    {
        SearchText.Parse(" Blade  Runner 2049 ").Text.ShouldBe("Blade Runner 2049");
    }
}
