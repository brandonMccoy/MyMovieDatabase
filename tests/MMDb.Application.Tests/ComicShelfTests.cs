using MMDb.Application.Comics;
using MMDb.Domain.Comics;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>The comics library: runs with their progress and gaps, the other shelves, search, filter and sort.</summary>
public sealed class ComicShelfTests : IAsyncLifetime
{
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        _shelf.Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]));
    }

    public async ValueTask DisposeAsync() => await _shelf.DisposeAsync();

    private async Task AddAsync(params ComicDetails[] comics)
    {
        foreach (var comic in comics)
        {
            await _shelf.AddComicAsync(comic);
            _shelf.Clock.Advance(TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Issues_are_grouped_into_runs_with_their_progress()
    {
        await AddAsync(TestComics.SagaIssue("1"), TestComics.Saga7, TestComics.Saga7 with { Variant = "Cover B", Barcode = null }, TestComics.Daredevil181);

        var shelf = await _shelf.ListComicsAsync();

        shelf.Runs.Select(r => (r.Title, r.Publisher, r.OwnedCount, r.KnownCount)).ShouldBe(
        [
            ("Daredevil (1964)", "Marvel", 1, null),
            ("Saga (2012)", "Image", 2, 10),
        ]);
        var saga = shelf.Runs[1];
        saga.Entries.Select(e => (e.Number, e.Through)).ShouldBe([("1", null), ("2", "6"), ("7", null)]);
        saga.Entries.Where(e => !e.IsMissing).Select(e => e.Copies.Count).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Gaps_are_only_shown_between_the_first_and_last_issue_owned()
    {
        await AddAsync(TestComics.SagaIssue("3"), TestComics.SagaIssue("5"));

        var saga = (await _shelf.ListComicsAsync()).Runs.ShouldHaveSingleItem();

        saga.Entries.Select(e => (e.Number, e.IsMissing)).ShouldBe([("3", false), ("4", true), ("5", false)]);
    }

    [Fact]
    public async Task Missing_issues_next_to_each_other_share_one_entry()
    {
        await AddAsync(TestComics.SagaIssue("1"), TestComics.SagaIssue("3"), TestComics.Saga7);

        var saga = (await _shelf.ListComicsAsync()).Runs.ShouldHaveSingleItem();

        saga.Entries.Select(e => (e.Number, e.Through, e.IsMissing)).ShouldBe(
            [("1", null, false), ("2", null, true), ("3", null, false), ("4", "6", true), ("7", null, false)]);
    }

    [Fact]
    public async Task While_searching_runs_show_only_what_matched()
    {
        await AddAsync(TestComics.SagaIssue("1"), TestComics.Saga7);

        var saga = (await _shelf.ListComicsAsync(new() { Search = "chapter seven" })).Runs.ShouldHaveSingleItem();

        saga.Entries.Select(e => e.Number).ShouldBe(["7"]);
    }

    [Fact]
    public async Task Trades_have_their_own_shelf_and_dont_count_toward_a_run()
    {
        await AddAsync(TestComics.SagaVolume1, TestComics.Saga7);

        var series = await _shelf.ListComicsAsync();
        var trades = await _shelf.ListComicsAsync(new() { View = ComicView.Collected });

        series.Runs.ShouldHaveSingleItem().OwnedCount.ShouldBe(1);
        trades.Comics.ShouldHaveSingleItem().DisplayName.ShouldBe("Saga, Volume One");
    }

    [Fact]
    public async Task Key_issues_and_all_issues_are_flat_lists()
    {
        await AddAsync(TestComics.Saga7, TestComics.Daredevil181, TestComics.SagaIssue("1"));

        (await _shelf.ListComicsAsync(new() { View = ComicView.KeyIssues })).Comics.Select(c => c.DisplayName).ShouldBe(["Daredevil #181"]);
        (await _shelf.ListComicsAsync(new() { View = ComicView.Issues })).Comics.Select(c => c.DisplayName).ShouldBe(["Daredevil #181", "Saga #1", "Saga #7"]);
    }

    [Fact]
    public async Task The_header_counts_the_whole_collection()
    {
        await AddAsync(TestComics.Saga7, TestComics.Saga7 with { Variant = "B", Barcode = null }, TestComics.Daredevil181, TestComics.SagaVolume1);

        var facets = (await _shelf.ListComicsAsync(new() { Search = "nothing like this" })).Facets;

        facets.ShouldBe(new ComicFacets(IssueCount: 3, SeriesCount: 2, CollectedCount: 1, KeyIssueCount: 1, Publishers: facets.Publishers));
        facets.Publishers.ShouldBe(["Image", "Image Comics", "Marvel"]);
    }

    [Theory]
    [InlineData("vaughan", "Saga #7")]
    [InlineData("ELEKTRA", "Daredevil #181")]
    [InlineData("miller janson", "Daredevil #181")]
    [InlineData("#181", "Daredevil #181")]
    [InlineData("1964", "Daredevil #181")]
    [InlineData("sága", "Saga #7")]
    public async Task Search_looks_at_series_creators_numbers_and_notes(string search, string found)
    {
        await AddAsync(TestComics.Saga7, TestComics.Daredevil181);

        var issues = await _shelf.ListComicsAsync(new() { View = ComicView.Issues, Search = search });

        issues.Comics.ShouldHaveSingleItem().DisplayName.ShouldBe(found);
    }

    [Fact]
    public async Task The_publisher_filter_narrows_every_shelf()
    {
        await AddAsync(TestComics.Saga7, TestComics.Daredevil181);

        (await _shelf.ListComicsAsync(new() { Publisher = "marvel" })).Runs.ShouldHaveSingleItem().Name.ShouldBe("Daredevil");
    }

    [Fact]
    public async Task Recently_added_puts_the_newest_first()
    {
        await AddAsync(TestComics.Saga7, TestComics.Daredevil181);

        var runs = await _shelf.ListComicsAsync(new() { Sort = ComicSort.RecentlyAdded });
        var issues = await _shelf.ListComicsAsync(new() { View = ComicView.Issues, Sort = ComicSort.RecentlyAdded });

        runs.Runs.Select(r => r.Name).ShouldBe(["Daredevil", "Saga"]);
        issues.Comics.Select(c => c.Series).ShouldBe(["Daredevil", "Saga"]);
    }

    [Fact]
    public async Task Series_sort_ignores_a_leading_the()
    {
        await AddAsync(TestComics.Issue("The Walking Dead", "1", 2003), TestComics.Issue("Saga", "1", 2012), TestComics.Issue("Batman", "1", 2016));

        (await _shelf.ListComicsAsync()).Runs.Select(r => r.Name).ShouldBe(["Batman", "Saga", "The Walking Dead"]);
    }
}
