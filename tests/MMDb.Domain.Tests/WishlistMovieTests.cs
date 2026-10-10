using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Domain.Tests;

public sealed class WishlistMovieTests
{
    private static readonly DateTimeOffset Sep2026 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct2026 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static WantedMovie Dune => new()
    {
        Title = "Dune",
        Year = 2021,
        Directors = ["Denis Villeneuve"],
        TmdbId = 438631,
        TrailerYouTubeKey = "8g18jFHCLXk",
        PosterPath = "/dune.jpg",
        Format = MediaFormat.Uhd4K,
    };

    [Fact]
    public void A_wanted_film_keeps_what_lists_it_and_the_format_wanted()
    {
        var item = WishlistMovie.Create(Dune, Sep2026);

        item.ShouldSatisfyAllConditions(
            i => i.Id.ShouldNotBe(Guid.Empty),
            i => i.Title.ShouldBe("Dune"),
            i => i.Year.ShouldBe(2021),
            i => i.Directors.ShouldBe(["Denis Villeneuve"]),
            i => i.TmdbId.ShouldBe(438631),
            i => i.TrailerYouTubeKey.ShouldBe("8g18jFHCLXk"),
            i => i.PosterPath.ShouldBe("/dune.jpg"),
            i => i.Format.ShouldBe(MediaFormat.Uhd4K),
            i => i.CreatedAt.ShouldBe(Sep2026),
            i => i.Version.ShouldBe(1));
    }

    [Fact]
    public void Text_is_tidied_like_a_film_in_the_collection()
    {
        var item = WishlistMovie.Create(Dune with { Title = "  Dune ", Directors = ["Denis Villeneuve", " ", "denis villeneuve"], TrailerYouTubeKey = " " }, Sep2026);

        item.Title.ShouldBe("Dune");
        item.Directors.ShouldBe(["Denis Villeneuve"]);
        item.TrailerYouTubeKey.ShouldBeNull();
    }

    [Fact]
    public void Wanting_it_again_replaces_the_format_and_keeps_when_it_was_first_wanted()
    {
        var item = WishlistMovie.Create(Dune, Sep2026);

        item.Want(Dune with { Format = MediaFormat.BluRay }, Oct2026);

        item.Format.ShouldBe(MediaFormat.BluRay);
        item.CreatedAt.ShouldBe(Sep2026);
        item.UpdatedAt.ShouldBe(Oct2026);
        item.Version.ShouldBe(2);
    }

    [Fact]
    public void Wanting_it_on_another_format_changes_nothing_else()
    {
        var item = WishlistMovie.Create(Dune, Sep2026);

        item.WantOn(MediaFormat.Dvd, Oct2026);

        item.ShouldSatisfyAllConditions(
            i => i.Format.ShouldBe(MediaFormat.Dvd),
            i => i.Title.ShouldBe("Dune"),
            i => i.PosterPath.ShouldBe("/dune.jpg"),
            i => i.TrailerYouTubeKey.ShouldBe("8g18jFHCLXk"),
            i => i.CreatedAt.ShouldBe(Sep2026),
            i => i.Version.ShouldBe(2));
        Should.Throw<DomainValidationException>(() => item.WantOn((MediaFormat)(-1), Oct2026));
    }

    [Fact]
    public void A_title_is_required()
    {
        WishlistMovie.Validate(Dune with { Title = " " }).ShouldHaveSingleError(MovieFields.Title);
        Should.Throw<DomainValidationException>(() => WishlistMovie.Create(Dune with { Title = " " }, Sep2026));
    }

    [Fact]
    public void The_format_must_be_one_there_is()
    {
        WishlistMovie.Validate(Dune with { Format = (MediaFormat)(-1) }).ShouldHaveSingleError(WishlistFields.Format);
    }

    [Fact]
    public void A_poster_must_be_one_of_tmdbs()
    {
        WishlistMovie.Validate(Dune with { PosterPath = "../secrets.jpg" }).ShouldHaveSingleError(MovieFields.PosterPath);
    }

    [Theory]
    [InlineData(438631, "Dune", 2021, true)]
    [InlineData(438631, "Dune: Part One", 2020, true)]
    [InlineData(841, "Dune", 2021, false)]
    [InlineData(null, "Dune", 2021, false)]
    public void A_wish_from_tmdb_is_for_the_film_with_the_same_tmdb_entry(int? tmdbId, string title, int year, bool expected)
    {
        WishlistMovie.Create(Dune, Sep2026).IsFor(tmdbId, title, year).ShouldBe(expected);
    }

    [Theory]
    [InlineData(438631, "Dune", 2021, true)]
    [InlineData(null, " dune ", 2021, true)]
    [InlineData(null, "Dune", 1984, false)]
    [InlineData(null, "Dune Messiah", 2021, false)]
    public void A_wish_entered_by_hand_is_for_the_film_with_the_same_title_and_year(int? tmdbId, string title, int year, bool expected)
    {
        WishlistMovie.Create(Dune with { TmdbId = null }, Sep2026).IsFor(tmdbId, title, year).ShouldBe(expected);
    }
}
