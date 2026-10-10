using MMDb.Application.Metadata;
using MMDb.Domain.Comics;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Finding a comic online from typed text, a cover barcode, or a trade's ISBN.</summary>
public sealed class ComicLookupTests : IAsyncLifetime
{
    private TestShelf _shelf = null!;

    private FakeMetron Metron => _shelf.Online.Metron;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public async ValueTask DisposeAsync() => await _shelf.DisposeAsync();

    private static VariantFacts CoverB => new("Cover B", "70985301744800721", "https://static.metron.cloud/media/variants/saga-7-b.jpg");

    [Fact]
    public async Task Typed_text_searches_metron_by_series_year_and_number()
    {
        Metron.Add(TestComics.Saga7).Add(TestComics.SagaIssue("1"));

        var result = await _shelf.LookupComicAsync("Saga (2012) #7");

        Metron.Searches.ShouldHaveSingleItem().ShouldBe(new IssueSearch { Series = "Saga", SeriesYear = 2012, Number = "7" });
        result.Outcome.ShouldBe(LookupOutcome.Found);
        result.Matches.ShouldHaveSingleItem().Details.DisplayName.ShouldBe("Saga #7");
    }

    [Fact]
    public async Task Each_variant_cover_is_a_match_of_its_own()
    {
        Metron.Add(TestComics.Saga7, CoverB);

        var result = await _shelf.LookupComicAsync("saga 7");

        result.Matches.Select(m => (m.Details.Variant, m.Details.Barcode, m.Details.CoverUrl)).ShouldBe(
        [
            (null, "70985301744800711", TestComics.Saga7.CoverUrl),
            ("Cover B", "70985301744800721", CoverB.CoverUrl),
        ]);
    }

    [Fact]
    public async Task Matches_say_which_variants_are_owned()
    {
        Metron.Add(TestComics.Saga7, CoverB);
        var owned = await _shelf.AddComicAsync(TestComics.Saga7);

        var result = await _shelf.LookupComicAsync("saga 7");

        result.Matches.Select(m => m.IsOwned).ShouldBe([true, false]);
        result.Matches[1].Owned.ShouldHaveSingleItem().Id.ShouldBe(owned.Id);
    }

    [Fact]
    public async Task A_scanned_barcode_finds_the_issue_and_goes_on_the_copy()
    {
        Metron.Add(TestComics.Saga7, CoverB);

        var result = await _shelf.LookupComicAsync("70985301744800711");

        Metron.Searches.ShouldHaveSingleItem().Barcode.ShouldBe("70985301744800711");
        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Variant.ShouldBeNull();
        result.Barcode.ShouldBe("70985301744800711");
    }

    [Fact]
    public async Task A_variants_barcode_picks_that_cover()
    {
        Metron.Add(TestComics.Saga7, CoverB);

        var result = await _shelf.LookupComicAsync("709853017448 00721");

        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Variant.ShouldBe("Cover B");
        match.Details.Barcode.ShouldBe("70985301744800721");
    }

    [Fact]
    public async Task A_barcode_metron_doesnt_list_is_found_by_its_upc_and_the_issue_in_its_add_on()
    {
        Metron.Add(TestComics.Saga7);

        var result = await _shelf.LookupComicAsync("70985301744800712");

        Metron.Searches[^1].ShouldBe(new IssueSearch { UpcPrefix = "709853017448", Number = "7" });
        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Barcode.ShouldBe("70985301744800712");
        match.Details.Printing.ShouldBe(2);
    }

    [Fact]
    public async Task The_upc_followed_by_a_typed_issue_number_is_searched_by_prefix()
    {
        Metron.Add(TestComics.Saga7);

        var result = await _shelf.LookupComicAsync("709853017448 #7");

        Metron.Searches.ShouldHaveSingleItem().ShouldBe(new IssueSearch { UpcPrefix = "709853017448", Number = "7" });
        result.Outcome.ShouldBe(LookupOutcome.Found);
    }

    [Fact]
    public async Task An_unknown_barcode_says_so()
    {
        var result = await _shelf.LookupComicAsync("70985301744800711");

        result.Outcome.ShouldBe(LookupOutcome.UnknownBarcode);
        result.Barcode.ShouldBe("70985301744800711");
    }

    [Fact]
    public async Task A_barcode_already_owned_links_to_the_copy_without_asking_metron()
    {
        var owned = await _shelf.AddComicAsync(TestComics.Saga7);

        var result = await _shelf.LookupComicAsync("70985301744800711");

        result.Outcome.ShouldBe(LookupOutcome.BarcodeOwned);
        result.OwnedByCode.ShouldNotBeNull().Id.ShouldBe(owned.Id);
        Metron.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_isbn_finds_the_trade_on_open_library()
    {
        _shelf.Online.Books.Add(TestComics.SagaVolume1);

        var result = await _shelf.LookupComicAsync("978-1-60706-601-9");

        var match = result.Matches.ShouldHaveSingleItem();
        match.Details.Format.ShouldBe(ComicFormat.TradePaperback);
        match.Details.Isbn.ShouldBe("9781607066019");
        Metron.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_isbn_already_owned_links_to_the_copy()
    {
        var owned = await _shelf.AddComicAsync(TestComics.SagaVolume1);

        var result = await _shelf.LookupComicAsync("1607066017");

        result.Outcome.ShouldBe(LookupOutcome.BarcodeOwned);
        result.OwnedByCode!.Id.ShouldBe(owned.Id);
        _shelf.Online.Books.Lookups.ShouldBe(0);
    }

    [Fact]
    public async Task An_isbn_open_library_doesnt_know_says_so()
    {
        (await _shelf.LookupComicAsync("9781607066019")).Outcome.ShouldBe(LookupOutcome.UnknownBarcode);
    }

    [Theory]
    [InlineData("Metron", MetadataProblem.NotConfigured, "saga 7")]
    [InlineData("Metron", MetadataProblem.Busy, "70985301744800711")]
    [InlineData("Open Library", MetadataProblem.Unavailable, "9781607066019")]
    public async Task A_database_that_cant_answer_is_reported(string service, MetadataProblem problem, string query)
    {
        Metron.FailWith = problem;
        _shelf.Online.Books.FailWith = problem;

        var result = await _shelf.LookupComicAsync(query);

        result.Outcome.ShouldBe(LookupOutcome.Failed);
        result.Problem.ShouldBe(problem);
        result.FailedService.ShouldBe(service);
    }

    [Fact]
    public async Task Only_a_few_issues_are_fetched()
    {
        for (var n = 1; n <= 10; n++)
        {
            Metron.Add(TestComics.SagaIssue(n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var result = await _shelf.LookupComicAsync("saga");

        result.Matches.Count.ShouldBe(ComicLookup.MaxIssues);
    }

    [Fact]
    public async Task Nothing_typed_finds_nothing()
    {
        (await _shelf.LookupComicAsync("  ")).Outcome.ShouldBe(LookupOutcome.NoMatches);
        Metron.Searches.ShouldBeEmpty();
    }
}
