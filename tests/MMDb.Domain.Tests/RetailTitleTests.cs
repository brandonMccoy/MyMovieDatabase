using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class RetailTitleTests
{
    [Theory]
    [InlineData("Heat [Blu-ray] (Widescreen)", "Heat")]
    [InlineData("Mill Creek - Fight Club [BLU-RAY]", "Fight Club")]
    [InlineData("Heat (4K Ultra HD + Blu-ray + Digital)", "Heat")]
    [InlineData("Alien 4K UHD", "Alien")]
    [InlineData("The Thing Collector's Edition Blu-ray", "The Thing")]
    [InlineData("Jaws 45th Anniversary Edition", "Jaws")]
    [InlineData("Blade Runner: The Final Cut (2-Disc Special Edition) [DVD]", "Blade Runner: The Final Cut")]
    [InlineData("Arrival - Steelbook", "Arrival")]
    [InlineData("Spirited Away Blu-ray + DVD Combo Pack", "Spirited Away")]
    [InlineData("Criterion Collection: Seven Samurai", "Seven Samurai")]
    [InlineData("The Godfather Widescreen DVD Region 1", "The Godfather")]
    [InlineData("  Heat  ", "Heat")]
    public void Packaging_words_are_removed_from_the_title(string productTitle, string title)
    {
        RetailTitle.Parse(productTitle).Title.ShouldBe(title);
    }

    [Theory]
    [InlineData("2001: A Space Odyssey [Blu-ray]", "2001: A Space Odyssey")]
    [InlineData("Blade Runner 2049 (Blu-ray)", "Blade Runner 2049")]
    [InlineData("Mission: Impossible - Fallout [DVD]", "Mission: Impossible - Fallout")]
    [InlineData("Limitless DVD", "Limitless")]
    [InlineData("The Bourne Ultimatum", "The Bourne Ultimatum")]
    [InlineData("Special", "Special")]
    public void Titles_that_merely_contain_numbers_or_dashes_are_kept(string productTitle, string title)
    {
        RetailTitle.Parse(productTitle).Title.ShouldBe(title);
    }

    [Theory]
    [InlineData("Heat [DVD]", MediaFormat.Dvd)]
    [InlineData("Mill Creek - Fight Club [BLU-RAY]", MediaFormat.BluRay)]
    [InlineData("Avatar Blu ray 3D", MediaFormat.BluRay)]
    [InlineData("Alien 4K UHD", MediaFormat.Uhd4K)]
    [InlineData("Heat (4K Ultra HD + Blu-ray + Digital)", MediaFormat.Uhd4K)]
    [InlineData("Dune Ultra HD", MediaFormat.Uhd4K)]
    [InlineData("Spirited Away Blu-ray + DVD Combo Pack", MediaFormat.BluRay)]
    public void The_format_comes_from_the_product_name_with_combo_packs_counting_as_the_best_disc(string productTitle, MediaFormat format)
    {
        RetailTitle.Parse(productTitle).Format.ShouldBe(format);
    }

    [Fact]
    public void Without_a_format_in_the_name_the_format_is_unknown()
    {
        RetailTitle.Parse("Heat").Format.ShouldBeNull();
    }

    [Theory]
    [InlineData("Heat (1995) [Blu-ray]", 1995)]
    [InlineData("The Thing [1982]", 1982)]
    [InlineData("Heat [Blu-ray]", null)]
    [InlineData("Blade Runner 2049 [Blu-ray]", null)]
    [InlineData("Heat (3000)", null)]
    public void A_year_in_brackets_is_the_release_year(string productTitle, int? year)
    {
        RetailTitle.Parse(productTitle).Year.ShouldBe(year);
    }

    [Theory]
    [InlineData("[Blu-ray]")]
    [InlineData("DVD")]
    public void A_name_that_is_all_packaging_is_kept_as_is(string productTitle)
    {
        RetailTitle.Parse(productTitle).Title.ShouldBe(productTitle);
    }
}
