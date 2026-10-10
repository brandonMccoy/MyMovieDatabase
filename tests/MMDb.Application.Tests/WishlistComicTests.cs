using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Application.Wishlist;
using MMDb.Domain.Comics;
using MMDb.Domain.Wishlist;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Comics on the wishlist: issues (also the ones missing from a run, each its own item) and collected editions.</summary>
public sealed class WishlistComicTests : IAsyncLifetime
{
    private const int Saga = 1234;

    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        _shelf.Online.Metron.AddSeries(Saga, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]));
        _shelf.Online.Metron.Add(TestComics.Saga7).Add(TestComics.SagaIssue("4")).Add(TestComics.SagaIssue("5"));
    }

    public async ValueTask DisposeAsync() => await _shelf.DisposeAsync();

    private static WantedComic Wanted(ComicDetails comic) => new()
    {
        Format = comic.Format,
        Series = comic.Series,
        SeriesYear = comic.SeriesYear,
        Number = comic.Number,
        Title = comic.Title,
        Publisher = comic.Publisher,
        Isbn = comic.Isbn,
        MetronId = comic.MetronId,
        MetronSeriesId = comic.MetronSeriesId,
        CoverUrl = comic.CoverUrl,
    };

    private async Task<WishlistComic> WantAsync(ComicDetails comic)
    {
        var result = await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(Wanted(comic), ct));
        return result.ShouldBeOfType<WantComicResult.Saved>().Comic;
    }

    private async Task<WishlistComic> WantMissingAsync(string from, string? through)
    {
        var result = await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, from, through, ct));
        return result.ShouldBeOfType<WantComicResult.Saved>().Comic;
    }

    private async Task<IReadOnlyList<string>> WantedNamesAsync() => [.. (await _shelf.WishlistAsync()).Comics.Select(c => c.DisplayName).Order()];

    private async Task<IEnumerable<(string Number, string? Through, bool Wanted)>> MissingAsync() =>
        (await _shelf.ListComicsAsync()).Runs.Single(r => r.Name == "Saga").Entries.Where(e => e.IsMissing).Select(e => (e.Number, e.Through, e.Wanted));

    [Fact]
    public async Task A_wanted_issue_is_listed_and_its_cover_is_kept()
    {
        var item = await WantAsync(TestComics.Saga7);

        var wishlist = await _shelf.WishlistAsync();
        wishlist.ComicCount.ShouldBe(1);
        wishlist.MovieCount.ShouldBe(0);
        wishlist.Comics.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Id.ShouldBe(item.Id),
            c => c.DisplayName.ShouldBe("Saga #7"),
            c => c.Publisher.ShouldBe("Image"),
            c => c.SeriesYear.ShouldBe(2012),
            c => c.FromMissing.ShouldBeFalse());
        _shelf.Online.Covers.Files.Keys.ShouldHaveSingleItem().ShouldBe(item.CoverImage);
        (await _shelf.ListComicsAsync(new() { View = ComicView.Issues })).Comics.ShouldBeEmpty("the wishlist is not the collection");
    }

    [Fact]
    public async Task One_wanted_comic_is_fetched_by_its_id()
    {
        var item = await WantAsync(TestComics.Saga7);

        (await _shelf.Wishlist((wishlist, ct) => wishlist.GetComicAsync(item.Id, ct))).ShouldNotBeNull().DisplayName.ShouldBe("Saga #7");
        (await _shelf.Wishlist((wishlist, ct) => wishlist.GetComicAsync(Guid.NewGuid(), ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Wanting_the_same_issue_twice_keeps_one_item()
    {
        var first = await WantAsync(TestComics.Saga7);

        var again = await WantAsync(TestComics.Saga7 with { Variant = "Cover B" });

        again.Id.ShouldBe(first.Id);
        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_issue_already_owned_is_not_wanted()
    {
        var owned = await _shelf.AddComicAsync(TestComics.Saga7 with { Variant = "Cover B", Barcode = null });

        var result = await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(Wanted(TestComics.Saga7), ct));

        result.ShouldBeOfType<WantComicResult.AlreadyOwned>().Comic.Id.ShouldBe(owned.Id);
        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_invalid_wish_is_refused_with_its_errors()
    {
        var result = await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(new WantedComic { Series = "Saga" }, ct));

        result.ShouldBeOfType<WantComicResult.Invalid>().Errors.ShouldHaveSingleItem().Field.ShouldBe(ComicFields.Number);
    }

    [Theory]
    [InlineData("saga", 2)]
    [InlineData("image", 2)]
    [InlineData("volume", 1)]
    [InlineData("2012", 1)]
    [InlineData("daredevil", 0)]
    public async Task Search_finds_wanted_comics_by_name_series_publisher_or_year(string search, int expected)
    {
        await WantAsync(TestComics.Saga7);
        await WantAsync(TestComics.SagaVolume1);

        var wishlist = await _shelf.WishlistAsync(search);

        wishlist.Comics.Count.ShouldBe(expected);
        wishlist.ComicCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_comic_no_longer_wanted_is_gone_with_its_cover()
    {
        var item = await WantAsync(TestComics.Saga7);

        (await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveComicAsync(item.Id, ct))).ShouldBeTrue();

        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(0);
        _shelf.Online.Covers.Files.ShouldBeEmpty();
        (await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveComicAsync(item.Id, ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Saving_any_copy_of_a_wanted_issue_takes_it_off_the_wishlist()
    {
        await WantAsync(TestComics.Saga7);
        await WantAsync(TestComics.SagaIssue("4"));

        var copy = await _shelf.AddComicAsync(TestComics.Saga7 with { Variant = "Cover B" });

        (await WantedNamesAsync()).ShouldBe(["Saga #4"]);
        _shelf.Online.Covers.Files.Keys.ShouldContain(copy.CoverImage!);
        _shelf.Online.Covers.Files.Count.ShouldBe(2, "the granted wish's cover is gone");
    }

    [Fact]
    public async Task Saving_a_wanted_book_takes_it_off_the_wishlist()
    {
        await WantAsync(TestComics.SagaVolume1);
        await WantAsync(TestComics.Saga7);

        await _shelf.AddComicAsync(TestComics.SagaVolume1 with { Format = ComicFormat.Hardcover });

        (await WantedNamesAsync()).ShouldBe(["Saga #7"]);
    }

    [Fact]
    public async Task Every_issue_of_a_missing_run_goes_on_the_wishlist_as_an_item_of_its_own()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        (await MissingAsync()).ShouldBe([("2", "6", false)]);

        var first = await WantMissingAsync("2", "6");

        first.DisplayName.ShouldBe("Saga #2");
        var wishlist = await _shelf.WishlistAsync();
        wishlist.ComicCount.ShouldBe(5);
        wishlist.Comics.Select(c => c.DisplayName).ShouldBe(["Saga #2", "Saga #3", "Saga #4", "Saga #5", "Saga #6"]);
        wishlist.Comics.ShouldAllBe(c => c.FromMissing && c.SeriesYear == 2012 && c.Publisher == "Image" && c.MetronSeriesId == Saga);
        (await MissingAsync()).ShouldBe([("2", "6", true)]);
    }

    [Fact]
    public async Task Missing_issues_get_the_cover_and_the_entry_metron_lists_for_them()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        var covers = _shelf.Online.Covers.Files.Count;

        await WantMissingAsync("2", "6");

        // Metron (the fake) lists #4, #5 and #7 of the series; the others it knows no cover for.
        var wanted = (await _shelf.WishlistAsync()).Comics;
        wanted.Select(c => (c.Number, c.MetronId, HasCover: c.CoverImage is not null)).ShouldBe(
        [
            ("2", null, false), ("3", null, false), ("4", 4518, true), ("5", 4519, true), ("6", null, false),
        ]);
        var four = wanted.Single(c => c.Number == "4");
        four.CoverUrl.ShouldBe("https://static.metron.cloud/media/issue/saga-4.jpg");
        _shelf.Online.Covers.Files[four.CoverImage!].ShouldBe(FakeComicCovers.ImageFor(four.CoverUrl!));
        _shelf.Online.Covers.Files.Count.ShouldBe(covers + 2);
    }

    [Fact]
    public async Task Missing_issues_are_wanted_without_covers_while_metron_is_down()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;

        await WantMissingAsync("4", "5");

        (await _shelf.WishlistAsync()).Comics.Select(c => (c.DisplayName, c.MetronId, c.CoverImage)).ShouldBe([("Saga #4", null, null), ("Saga #5", null, null)]);
    }

    [Fact]
    public async Task Wanting_missing_issues_again_downloads_nothing_more()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("6"));
        await WantMissingAsync("4", "5");
        var fetched = _shelf.Online.ComicCovers.Fetched.Count;

        await WantMissingAsync("4", "5");

        _shelf.Online.ComicCovers.Fetched.Count.ShouldBe(fetched);
        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(2);
    }

    [Fact]
    public async Task One_missing_issue_is_wanted_like_any_issue()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("5"));
        await _shelf.AddComicAsync(TestComics.Saga7);

        (await WantMissingAsync("6", null)).DisplayName.ShouldBe("Saga #6");

        (await MissingAsync()).ShouldBe([("6", null, true)]);
    }

    [Fact]
    public async Task Buying_one_of_the_missing_issues_takes_only_that_one_off_the_wishlist()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        await WantMissingAsync("2", "6");

        await _shelf.AddComicAsync(TestComics.SagaIssue("4"));

        (await WantedNamesAsync()).ShouldBe(["Saga #2", "Saga #3", "Saga #5", "Saga #6"]);
        (await MissingAsync()).ShouldBe([("2", "3", true), ("5", "6", true)]);
    }

    [Fact]
    public async Task Dropping_one_of_the_missing_issues_splits_the_tile_where_the_wishlist_stops()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        await WantMissingAsync("2", "6");
        var four = (await _shelf.WishlistAsync()).Comics.Single(c => c.Number == "4");

        await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveComicAsync(four.Id, ct));

        (await MissingAsync()).ShouldBe([("2", "3", true), ("4", null, false), ("5", "6", true)]);
    }

    [Fact]
    public async Task Buying_every_missing_issue_empties_the_wishlist()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("6"));
        await WantMissingAsync("4", "5");

        await _shelf.AddComicAsync(TestComics.SagaIssue("5"));
        (await WantedNamesAsync()).ShouldBe(["Saga #4"]);

        await _shelf.AddComicAsync(TestComics.SagaIssue("4"));
        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_issue_of_the_run_wanted_already_stays_as_it_is()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
        var four = await WantAsync(TestComics.SagaIssue("4"));
        (await MissingAsync()).ShouldBe([("2", "3", false), ("4", null, true), ("5", "6", false)]);

        await WantMissingAsync("2", "6");
        await WantMissingAsync("2", "6");

        var wanted = (await _shelf.WishlistAsync()).Comics;
        wanted.Select(c => c.DisplayName).Order().ShouldBe(["Saga #2", "Saga #3", "Saga #4", "Saga #5", "Saga #6"]);
        wanted.Single(c => c.Number == "4").ShouldSatisfyAllConditions(
            c => c.Id.ShouldBe(four.Id),
            c => c.FromMissing.ShouldBeFalse(),
            c => c.CoverImage.ShouldNotBeNull("it keeps the cover it was wanted with"));
    }

    [Fact]
    public async Task Missing_issues_of_a_series_nobody_knows_cannot_be_wanted()
    {
        var unknownSeries = await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(999, "2", "6", ct));
        await _shelf.AddComicAsync(TestComics.Saga7);
        var unknownIssue = await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, "2", "60", ct));

        unknownSeries.ShouldBeOfType<WantComicResult.NotFound>();
        unknownIssue.ShouldBeOfType<WantComicResult.NotFound>();
        (await _shelf.WishlistAsync()).ComicCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_lookup_says_which_matches_are_wanted_also_from_missing_issues()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("6"));
        await WantMissingAsync("2", "5");
        var run = (await _shelf.WishlistAsync()).Comics.Single(c => c.Number == "4");

        var four = await _shelf.LookupComicAsync("saga 4");
        var seven = await _shelf.LookupComicAsync("saga 7");
        var scanned = await _shelf.ScanAsync(TestComics.Saga7.Barcode!);

        four.Matches.ShouldHaveSingleItem().Wanted!.Id.ShouldBe(run.Id);
        seven.Matches.ShouldHaveSingleItem().Wanted.ShouldBeNull();
        scanned.Comic!.Matches.ShouldHaveSingleItem().Wanted.ShouldBeNull();
    }

    [Fact]
    public async Task Got_it_looks_a_wanted_issue_up_by_its_metron_entry_without_searching()
    {
        var item = await WantAsync(TestComics.Saga7);

        var result = (await _shelf.LookupWishComicAsync(item.Id)).ShouldNotBeNull();

        result.Outcome.ShouldBe(LookupOutcome.Found);
        result.Wish!.Id.ShouldBe(item.Id);
        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Writers.ShouldBe(["Brian K. Vaughan"]);
        match.Wanted!.Id.ShouldBe(item.Id);
        _shelf.Online.Metron.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_for_a_missing_issue_opens_the_entry_metron_listed_for_it()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("6"));
        await WantMissingAsync("4", "5");
        var five = (await _shelf.WishlistAsync()).Comics.Single(c => c.Number == "5");

        var result = (await _shelf.LookupWishComicAsync(five.Id)).ShouldNotBeNull();

        result.Matches.ShouldHaveSingleItem().Details.MetronId.ShouldBe(4519);
        _shelf.Online.Metron.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_for_an_issue_without_a_metron_entry_searches_for_its_series_and_number()
    {
        var five = await WantAsync(TestComics.SagaIssue("5") with { MetronId = null });

        var result = (await _shelf.LookupWishComicAsync(five.Id)).ShouldNotBeNull();

        result.Matches.ShouldHaveSingleItem().Details.Number.ShouldBe("5");
        _shelf.Online.Metron.Searches.ShouldHaveSingleItem().ShouldBe(new IssueSearch { Series = "Saga", SeriesYear = 2012, Number = "5" });
    }

    [Fact]
    public async Task Got_it_for_a_book_wanted_without_an_isbn_finds_nothing_to_start_from()
    {
        var book = await WantAsync(TestComics.SagaVolume1 with { Isbn = null });

        var result = (await _shelf.LookupWishComicAsync(book.Id)).ShouldNotBeNull();

        result.Outcome.ShouldBe(LookupOutcome.NoMatches);
        result.Wish!.DisplayName.ShouldBe("Saga, Volume One");
        _shelf.Online.Metron.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Got_it_for_a_book_looks_its_isbn_up()
    {
        _shelf.Online.Books.Add(TestComics.SagaVolume1);
        var item = await WantAsync(TestComics.SagaVolume1);

        var result = (await _shelf.LookupWishComicAsync(item.Id)).ShouldNotBeNull();

        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Title.ShouldBe("Saga, Volume One");
        match.Wanted!.Id.ShouldBe(item.Id);
    }

    [Fact]
    public async Task Got_it_while_metron_is_down_still_says_which_wish_it_was()
    {
        var item = await WantAsync(TestComics.Saga7);
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;

        var result = (await _shelf.LookupWishComicAsync(item.Id)).ShouldNotBeNull();

        result.Outcome.ShouldBe(LookupOutcome.Failed);
        result.Wish!.Id.ShouldBe(item.Id);
        (await _shelf.LookupWishComicAsync(Guid.NewGuid())).ShouldBeNull();
    }
}
