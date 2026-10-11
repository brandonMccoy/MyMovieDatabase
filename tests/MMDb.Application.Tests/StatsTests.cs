using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Application.Stats;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>The Stats screen's numbers. The clock starts on 15 January 2026.</summary>
public sealed class StatsTests : IAsyncLifetime
{
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public ValueTask DisposeAsync() => _shelf.DisposeAsync();

    private static MovieDetails Film(string title, int? year = null, params string[] directors) => new() { Title = title, Year = year, Directors = directors };

    private static ComicDetails Issue(string series, string number, string? publisher) => TestComics.Issue(series, number) with { Publisher = publisher };

    private void GoTo(int year, int month, int day) => _shelf.Clock.SetUtcNow(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_empty_collection_counts_nothing_and_still_has_its_axes()
    {
        var stats = await _shelf.StatsAsync();

        stats.FilmCount.ShouldBe(0);
        stats.DiscCount.ShouldBe(0);
        stats.DigitalCount.ShouldBe(0);
        stats.IssueCount.ShouldBe(0);
        stats.SeriesCount.ShouldBe(0);
        stats.Formats.ShouldBe([new(MediaFormat.Uhd4K, 0), new(MediaFormat.BluRay, 0), new(MediaFormat.Dvd, 0), new(MediaFormat.Digital, 0)]);
        stats.AddedByMonth.Count.ShouldBe(12);
        stats.AddedByMonth.ShouldAllBe(m => m.Count == 0);
        stats.Decades.ShouldBeEmpty();
        stats.TopDirectors.ShouldBeEmpty();
        stats.Publishers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Films_discs_and_digital_copies_are_counted_apart()
    {
        await _shelf.AddAsync(Film("Heat"), MediaFormat.Uhd4K, MediaFormat.Dvd, MediaFormat.Digital);
        await _shelf.AddAsync(Film("Alien"), MediaFormat.BluRay);
        await _shelf.AddAsync(Film("Arrival"), MediaFormat.Digital);

        var stats = await _shelf.StatsAsync();

        stats.FilmCount.ShouldBe(3);
        stats.DiscCount.ShouldBe(3);
        stats.DigitalCount.ShouldBe(2);
        stats.FilmsOnSeveralFormats.ShouldBe(1);
        stats.Formats.ShouldBe([new(MediaFormat.Uhd4K, 1), new(MediaFormat.BluRay, 1), new(MediaFormat.Dvd, 1), new(MediaFormat.Digital, 2)]);
    }

    [Fact]
    public async Task This_month_counts_what_joined_since_the_first()
    {
        GoTo(2026, 1, 31);
        await _shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);
        await _shelf.AddComicAsync(TestComics.Issue("Saga", "1"));
        GoTo(2026, 2, 1);
        await _shelf.AddAsync(Film("Alien"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Arrival"), MediaFormat.Dvd);
        await _shelf.AddComicAsync(TestComics.Issue("Saga", "2"));
        GoTo(2026, 2, 20);

        var stats = await _shelf.StatsAsync();

        stats.FilmsAddedThisMonth.ShouldBe(2);
        stats.IssuesAddedThisMonth.ShouldBe(1);
    }

    [Fact]
    public async Task Each_copy_counts_in_the_month_it_was_added_for_the_last_twelve()
    {
        GoTo(2026, 1, 31);
        await _shelf.AddAsync(Film("Too long ago"), MediaFormat.Dvd);
        GoTo(2026, 2, 1);
        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd, MediaFormat.Digital);
        GoTo(2026, 11, 5);
        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K); // the film is owned: only the format is new
        await _shelf.AddComicAsync(TestComics.Saga7);
        await _shelf.AddComicAsync(TestComics.SagaVolume1);
        GoTo(2027, 1, 15);
        await _shelf.AddAsync(Film("Alien"), MediaFormat.BluRay);

        var months = (await _shelf.StatsAsync()).AddedByMonth;

        months.Select(m => (m.Year, m.Month)).ShouldBe(
            [(2026, 2), (2026, 3), (2026, 4), (2026, 5), (2026, 6), (2026, 7), (2026, 8), (2026, 9), (2026, 10), (2026, 11), (2026, 12), (2027, 1)]);
        months.Select(m => m.Count).ShouldBe([2, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 1]);
    }

    [Fact]
    public async Task Months_begin_and_end_by_the_pcs_clock()
    {
        _shelf.Clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-6), "test", "test"));
        _shelf.Clock.SetUtcNow(new DateTimeOffset(2026, 2, 1, 3, 0, 0, TimeSpan.Zero)); // 9 pm on 31 January there
        await _shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);

        var stats = await _shelf.StatsAsync();

        stats.FilmsAddedThisMonth.ShouldBe(1);
        stats.AddedByMonth[^1].ShouldBe(new MonthCount(2026, 1, 1));
    }

    [Fact]
    public async Task Decades_run_from_the_oldest_film_to_the_newest_without_gaps()
    {
        await _shelf.AddAsync(Film("Alien", 1979), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Heat", 1995), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Fargo", 1996), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Arrival", 2016), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Home video"), MediaFormat.Digital);

        var stats = await _shelf.StatsAsync();

        stats.Decades.ShouldBe([new(1970, 1), new(1980, 0), new(1990, 2), new(2000, 0), new(2010, 1)]);
        stats.FilmsWithoutYear.ShouldBe(1);
    }

    [Fact]
    public async Task The_six_directors_with_the_most_films_lead_and_ties_go_by_name()
    {
        string[] oneEach = ["Ridley Scott", "John Carpenter", "Agnès Varda", "Bong Joon Ho", "Céline Sciamma"];
        foreach (var director in oneEach)
        {
            await _shelf.AddAsync(Film("By " + director, 2000, director), MediaFormat.Dvd);
        }

        await _shelf.AddAsync(Film("Heat", 1995, "Michael Mann"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Collateral", 2004, "michael mann"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Fargo", 1996, "Joel Coen", "Ethan Coen"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("No Country for Old Men", 2007, "Joel Coen", "Ethan Coen"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Blood Simple", 1984, "Joel Coen"), MediaFormat.Dvd);

        var directors = (await _shelf.StatsAsync()).TopDirectors;

        directors.ShouldBe(
        [
            new("Joel Coen", 3),
            new("Ethan Coen", 2),
            new("Michael Mann", 2),
            new("Agnès Varda", 1),
            new("Bong Joon Ho", 1),
            new("Céline Sciamma", 1),
        ]);
    }

    [Fact]
    public async Task A_directors_count_is_what_the_library_lists_for_that_director()
    {
        await _shelf.AddAsync(Film("Heat", 1995, "Michael Mann"), MediaFormat.Dvd);
        await _shelf.AddAsync(Film("Collateral", 2004, "MICHAEL MANN"), MediaFormat.Dvd);
        await _shelf.AddAsync(TestMovies.Titled("Hancock", 2008) with { Directors = ["Peter Berg"], Crew = [new("Michael Mann", "Producer", "Production")] }, MediaFormat.Dvd);

        var mann = (await _shelf.StatsAsync()).TopDirectors[0];
        var listed = await _shelf.ListAsync(new() { Director = mann.Name });

        mann.Count.ShouldBe(2);
        listed.Movies.Select(m => m.Title).ShouldBe(["Collateral", "Heat"], ignoreOrder: true);
    }

    [Fact]
    public async Task Publishers_count_single_issues_most_first()
    {
        await _shelf.AddComicAsync(Issue("Daredevil", "181", "Marvel"));
        await _shelf.AddComicAsync(Issue("Daredevil", "182", "marvel"));
        await _shelf.AddComicAsync(Issue("Saga", "1", "Image"));
        await _shelf.AddComicAsync(TestComics.SagaVolume1); // a trade: not an issue

        var stats = await _shelf.StatsAsync();

        stats.Publishers.ShouldBe([new("Marvel", 2), new("Image", 1)]);
        stats.IssueCount.ShouldBe(3);
        stats.CollectedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Six_publishers_fit_and_a_seventh_makes_the_smallest_ones_the_rest()
    {
        string[] publishers = ["Marvel", "DC", "Image", "Dark Horse", "BOOM!", "IDW"];
        foreach (var (publisher, i) in publishers.Select((p, i) => (p, i)))
        {
            for (var n = 0; n < publishers.Length - i; n++)
            {
                await _shelf.AddComicAsync(Issue(publisher + " Presents", (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), publisher));
            }
        }

        (await _shelf.StatsAsync()).Publishers.Select(p => (p.Name, p.Count)).ShouldBe(
            [("Marvel", 6), ("DC", 5), ("Image", 4), ("Dark Horse", 3), ("BOOM!", 2), ("IDW", 1)]);

        await _shelf.AddComicAsync(Issue("Zine", "1", "Self-published"));
        await _shelf.AddComicAsync(Issue("Mystery", "1", null));

        (await _shelf.StatsAsync()).Publishers.Select(p => (p.Name, p.Count)).ShouldBe(
            [("Marvel", 6), ("DC", 5), ("Image", 4), ("Dark Horse", 3), ("BOOM!", 2), (null, 3)]);
    }

    [Fact]
    public async Task A_run_is_complete_when_every_issue_metron_lists_is_owned()
    {
        _shelf.Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3"]));
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("2"));
        await _shelf.AddComicAsync(TestComics.Daredevil181); // Metron doesn't know this run: never complete

        var before = await _shelf.StatsAsync();
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("3") with { Variant = "Cover B" });
        var after = await _shelf.StatsAsync();

        before.SeriesCount.ShouldBe(2);
        before.CompleteSeriesCount.ShouldBe(0);
        after.SeriesCount.ShouldBe(2);
        after.CompleteSeriesCount.ShouldBe(1);
        var library = await _shelf.ListComicsAsync();
        library.Runs.Count(r => r.KnownCount == r.OwnedCount).ShouldBe(after.CompleteSeriesCount, "the library's own \"3 of 3\"");
        library.Facets.SeriesCount.ShouldBe(after.SeriesCount);
    }

    [Fact]
    public async Task The_wishlist_is_counted_by_kind()
    {
        await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(new WantedComic { Series = "Saga", Number = "8" }, ct));
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(new WantedComic { Series = "Saga", Number = "9" }, ct));

        var stats = await _shelf.StatsAsync();

        stats.WishlistMovieCount.ShouldBe(1);
        stats.WishlistComicCount.ShouldBe(2);
    }
}
