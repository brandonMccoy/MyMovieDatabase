using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class TrailerTests
{
    [Fact]
    public void A_stored_youtube_key_links_straight_to_the_video()
    {
        Trailer.UrlFor("Heat", 1995, "0xbBLJ1WGwQ").ShouldBe(new Uri("https://www.youtube.com/watch?v=0xbBLJ1WGwQ"));
    }

    [Fact]
    public void Without_a_key_it_searches_youtube_for_title_year_and_trailer()
    {
        Trailer.UrlFor("Heat", 1995, null).AbsoluteUri.ShouldBe("https://www.youtube.com/results?search_query=Heat%201995%20trailer");
    }

    [Fact]
    public void The_search_leaves_out_a_missing_year_and_escapes_the_title()
    {
        Trailer.UrlFor("Mad Max: Fury Road & more", null, " ").AbsoluteUri
            .ShouldBe("https://www.youtube.com/results?search_query=Mad%20Max%3A%20Fury%20Road%20%26%20more%20trailer");
    }
}
