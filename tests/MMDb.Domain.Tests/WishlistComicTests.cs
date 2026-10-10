using MMDb.Domain.Comics;
using MMDb.Domain.Wishlist;

namespace MMDb.Domain.Tests;

public sealed class WishlistComicTests
{
    private static readonly DateTimeOffset Sep2026 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static WantedComic Saga7 => new()
    {
        Series = "Saga",
        SeriesYear = 2012,
        Number = "7",
        Publisher = "Image",
        MetronId = 4521,
        MetronSeriesId = 1234,
        CoverUrl = "https://static.metron.cloud/media/issue/saga-7.jpg",
    };

    private static WantedComic SagaTrade => new() { Format = ComicFormat.TradePaperback, Title = "Saga, Volume One", Series = "Saga", Number = "1", Isbn = "978-1-60706-601-9" };

    private static ComicDetails Issue(string series, string number, int? year = null, int? metronSeriesId = null) =>
        new() { Series = series, Number = number, SeriesYear = year, MetronSeriesId = metronSeriesId };

    [Fact]
    public void A_wanted_issue_keeps_what_lists_it()
    {
        var item = WishlistComic.Create(Saga7 with { Number = "#007", Series = " Saga ", FromMissing = true }, Sep2026);

        item.ShouldSatisfyAllConditions(
            i => i.DisplayName.ShouldBe("Saga #7"),
            i => i.Format.ShouldBe(ComicFormat.SingleIssue),
            i => i.Series.ShouldBe("Saga"),
            i => i.SeriesYear.ShouldBe(2012),
            i => i.Number.ShouldBe("7"),
            i => i.Publisher.ShouldBe("Image"),
            i => i.MetronId.ShouldBe(4521),
            i => i.MetronSeriesId.ShouldBe(1234),
            i => i.CoverUrl.ShouldBe("https://static.metron.cloud/media/issue/saga-7.jpg"),
            i => i.FromMissing.ShouldBeTrue(),
            i => i.CreatedAt.ShouldBe(Sep2026),
            i => i.Version.ShouldBe(1));
    }

    [Fact]
    public void A_collected_edition_is_named_by_its_title_or_its_volume()
    {
        WishlistComic.Create(SagaTrade, Sep2026).ShouldSatisfyAllConditions(
            i => i.DisplayName.ShouldBe("Saga, Volume One"),
            i => i.Isbn.ShouldBe("9781607066019"));
        WishlistComic.Create(SagaTrade with { Title = null, Isbn = null }, Sep2026).DisplayName.ShouldBe("Saga Vol. 1");
    }

    [Fact]
    public void An_issue_needs_a_series_and_a_number()
    {
        WishlistComic.Validate(Saga7 with { Series = " " }).ShouldHaveSingleError(ComicFields.Series);
        WishlistComic.Validate(Saga7 with { Number = null }).ShouldHaveSingleError(ComicFields.Number);
        WishlistComic.Validate(Saga7 with { Number = "a number far too long to be one" }).ShouldHaveSingleError(ComicFields.Number);
        Should.Throw<DomainValidationException>(() => WishlistComic.Create(Saga7 with { Number = null }, Sep2026));
    }

    [Fact]
    public void A_collected_edition_needs_a_title_or_a_series()
    {
        WishlistComic.Validate(new WantedComic { Format = ComicFormat.Hardcover }).ShouldHaveSingleError(ComicFields.Title);
        WishlistComic.Validate(SagaTrade with { Isbn = "123" }).ShouldHaveSingleError(ComicFields.Isbn);
    }

    [Theory]
    [InlineData("Saga", "7", 2012, 1234, true)]
    [InlineData("SAGA", "7", null, null, true)]
    [InlineData("Saga (the other one)", "7", 2012, 1234, true)]
    [InlineData("Saga", "7", 2012, 999, false)]
    [InlineData("Saga", "8", 2012, 1234, false)]
    [InlineData("Saga", "7", 1990, null, false)]
    [InlineData("Sagas", "7", 2012, null, false)]
    public void A_wanted_issue_is_any_copy_of_that_issue_of_that_series(string series, string number, int? year, int? metronSeriesId, bool expected)
    {
        var wish = WishlistComic.Create(Saga7, Sep2026);

        wish.IsFor(Issue(series, number, year, metronSeriesId) with { Variant = "Cover B", Printing = 2 }).ShouldBe(expected);
        wish.IsIssue(metronSeriesId, series, year, number).ShouldBe(expected);
    }

    [Fact]
    public void An_issue_is_not_a_collected_edition_of_the_same_name()
    {
        WishlistComic.Create(Saga7, Sep2026).IsFor(new() { Format = ComicFormat.TradePaperback, Series = "Saga", Number = "7" }).ShouldBeFalse();
        WishlistComic.Create(SagaTrade, Sep2026).IsFor(Issue("Saga", "1")).ShouldBeFalse();
        WishlistComic.Create(SagaTrade, Sep2026).IsIssue(null, "Saga", null, "1").ShouldBeFalse();
    }

    [Fact]
    public void A_wanted_book_is_the_one_with_its_isbn_or_else_its_title()
    {
        var wanted = WishlistComic.Create(SagaTrade, Sep2026);
        var byTitle = WishlistComic.Create(SagaTrade with { Isbn = null }, Sep2026);
        ComicDetails book = new() { Format = ComicFormat.Hardcover, Title = "saga, volume one", Isbn = "9781607066019" };

        wanted.IsFor(book).ShouldBeTrue();
        wanted.IsFor(book with { Isbn = "9781632154385" }).ShouldBeFalse("another book, whatever it is called");
        byTitle.IsFor(book).ShouldBeTrue();
        byTitle.IsFor(book with { Title = "Saga, Volume Two" }).ShouldBeFalse();
        WishlistComic.Create(SagaTrade with { Isbn = null, Title = null }, Sep2026).IsFor(new() { Format = ComicFormat.TradePaperback, Series = "Saga", Number = "1" }).ShouldBeTrue();
    }
}
