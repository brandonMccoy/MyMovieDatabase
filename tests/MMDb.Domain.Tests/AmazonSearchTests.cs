using MMDb.Domain.Wishlist;

namespace MMDb.Domain.Tests;

public sealed class AmazonSearchTests
{
    [Theory]
    [InlineData(MediaFormat.Uhd4K, "https://www.amazon.com/s?k=Heat%201995%204K%20UHD&i=movies-tv")]
    [InlineData(MediaFormat.BluRay, "https://www.amazon.com/s?k=Heat%201995%20Blu-ray&i=movies-tv")]
    [InlineData(MediaFormat.Dvd, "https://www.amazon.com/s?k=Heat%201995%20DVD&i=movies-tv")]
    public void A_film_is_searched_for_in_movies_and_tv_by_title_year_and_the_disc_wanted(MediaFormat format, string expected)
    {
        AmazonSearch.ForMovie("Heat", 1995, format).ShouldNotBeNull().AbsoluteUri.ShouldBe(expected);
    }

    [Fact]
    public void A_film_wanted_on_digital_has_no_disc_to_search_for()
    {
        AmazonSearch.ForMovie("Heat", 1995, MediaFormat.Digital).ShouldBeNull();
    }

    [Fact]
    public void The_search_leaves_out_a_missing_year_and_escapes_the_title()
    {
        AmazonSearch.ForMovie(" Mad Max: Fury Road & more ", null, MediaFormat.BluRay).ShouldNotBeNull().AbsoluteUri
            .ShouldBe("https://www.amazon.com/s?k=Mad%20Max%3A%20Fury%20Road%20%26%20more%20Blu-ray&i=movies-tv");
    }

    [Fact]
    public void An_issue_is_searched_for_by_its_name_as_a_comic()
    {
        AmazonSearch.ForComic("Saga #7", isSingleIssue: true).AbsoluteUri.ShouldBe("https://www.amazon.com/s?k=Saga%20%237%20comic");
    }

    [Fact]
    public void A_collected_edition_is_searched_for_by_its_name_alone()
    {
        AmazonSearch.ForComic("Saga Vol. 1", isSingleIssue: false).AbsoluteUri.ShouldBe("https://www.amazon.com/s?k=Saga%20Vol.%201");
    }
}
