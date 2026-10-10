using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Testing;

namespace MMDb.Application.Tests;

public sealed class MovieLookupTests
{
    private static async Task<TestShelf> ShelfWithHeatsAsync()
    {
        var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb).Add(TestMovies.HeatAndDustOnTmdb);
        return shelf;
    }

    [Fact]
    public async Task A_title_search_returns_every_match_in_full_best_first()
    {
        await using var shelf = await ShelfWithHeatsAsync();

        var result = await shelf.LookupAsync("heat");

        result.Outcome.ShouldBe(LookupOutcome.Found);
        result.Matches.Select(m => (m.Details.Title, m.Details.Year)).ShouldBe([("Heat", 1995), ("Heat", 1986), ("Heat and Dust", 1983)]);
        result.Matches[0].Details.ShouldSatisfyAllConditions(
            d => d.Directors.ShouldBe(["Michael Mann"]),
            d => d.Cast.ShouldBe(TestMovies.Heat.Cast),
            d => d.Crew.ShouldBe(TestMovies.HeatCrew),
            d => d.PosterPath.ShouldBe("/heat.jpg"),
            d => d.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ"));
        result.Matches.ShouldAllBe(m => m.Owned == null);
    }

    [Fact]
    public async Task A_year_after_the_title_narrows_the_search()
    {
        await using var shelf = await ShelfWithHeatsAsync();

        var result = await shelf.LookupAsync("heat 1995");

        result.Matches.ShouldHaveSingleItem().Details.TmdbId.ShouldBe(949);
        shelf.Online.Tmdb.Searches.ShouldBe([("heat", 1995)]);
    }

    [Fact]
    public async Task A_title_that_ends_in_a_year_is_found_on_a_second_try_without_the_year()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Tmdb.Add(new() { Title = "Blade Runner 2049", Year = 2017, TmdbId = 335984 });

        var result = await shelf.LookupAsync("Blade Runner 2049");

        result.Matches.ShouldHaveSingleItem().Details.Title.ShouldBe("Blade Runner 2049");
        shelf.Online.Tmdb.Searches.ShouldBe([("Blade Runner", 2049), ("Blade Runner 2049", null)]);
    }

    [Fact]
    public async Task Nothing_found_says_so()
    {
        await using var shelf = await ShelfWithHeatsAsync();

        (await shelf.LookupAsync("Jaws")).Outcome.ShouldBe(LookupOutcome.NoMatches);
    }

    [Fact]
    public async Task A_blank_search_asks_nobody()
    {
        await using var shelf = await ShelfWithHeatsAsync();

        (await shelf.LookupAsync("   ")).Outcome.ShouldBe(LookupOutcome.NoMatches);
        shelf.Online.Tmdb.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_most_eight_matches_are_fetched()
    {
        await using var shelf = await TestShelf.CreateAsync();
        for (var i = 1; i <= 12; i++)
        {
            shelf.Online.Tmdb.Add(new() { Title = $"Halloween {i}", TmdbId = i });
        }

        var result = await shelf.LookupAsync("halloween");

        result.Matches.Count.ShouldBe(MovieLookup.MaxMatches);
        shelf.Online.Tmdb.Fetches.ShouldBe(MovieLookup.MaxMatches);
    }

    [Fact]
    public async Task Matches_already_in_the_collection_point_to_the_film_owned()
    {
        await using var shelf = await ShelfWithHeatsAsync();
        var owned = await shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        var result = await shelf.LookupAsync("heat");

        result.Matches[0].Owned.ShouldNotBeNull().Id.ShouldBe(owned.Id);
        result.Matches.Skip(1).ShouldAllBe(m => m.Owned == null);
    }

    [Fact]
    public async Task A_barcode_is_looked_up_by_its_cleaned_product_name_and_gives_the_format()
    {
        await using var shelf = await ShelfWithHeatsAsync();
        shelf.Online.Barcodes.Add("786936894424", "Heat (1995) [4K UHD + Blu-ray]");

        var result = await shelf.LookupAsync("786936894424");

        result.Outcome.ShouldBe(LookupOutcome.Found);
        result.Upc.ShouldBe(Upc.Parse("786936894424"));
        result.ProductName.ShouldBe("Heat (1995) [4K UHD + Blu-ray]");
        result.ProductTitle.ShouldBe("Heat");
        result.Format.ShouldBe(MediaFormat.Uhd4K);
        result.Matches.ShouldHaveSingleItem().Details.TmdbId.ShouldBe(949);
        shelf.Online.Tmdb.Searches.ShouldBe([("Heat", 1995)]);
    }

    [Fact]
    public async Task A_barcode_nobody_knows_says_so_and_keeps_the_code()
    {
        await using var shelf = await ShelfWithHeatsAsync();

        var result = await shelf.LookupAsync("036000291452");

        result.Outcome.ShouldBe(LookupOutcome.UnknownBarcode);
        result.Upc.ShouldBe(Upc.Parse("036000291452"));
        result.ProductTitle.ShouldBeNull();
        result.Format.ShouldBeNull();
        shelf.Online.Tmdb.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_barcode_with_no_film_on_tmdb_keeps_the_product_name_and_the_title_and_format_read_from_it()
    {
        await using var shelf = await ShelfWithHeatsAsync();
        shelf.Online.Barcodes.Add("036000291452", "Some Documentary [DVD]");

        var result = await shelf.LookupAsync("036000291452");

        result.Outcome.ShouldBe(LookupOutcome.NoMatches);
        result.ProductName.ShouldBe("Some Documentary [DVD]");
        result.ProductTitle.ShouldBe("Some Documentary");
        result.Format.ShouldBe(MediaFormat.Dvd);
    }

    [Theory]
    [InlineData("786936894424")]
    [InlineData("0786936894424")]
    public async Task A_barcode_already_on_the_shelf_is_recognised_without_asking_anyone(string scanned)
    {
        await using var shelf = await ShelfWithHeatsAsync();
        var owned = await shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.Heat, [new(MediaFormat.Uhd4K, Upc: "786936894424")], ct));

        var result = await shelf.LookupAsync(scanned);

        result.Outcome.ShouldBe(LookupOutcome.BarcodeOwned);
        result.OwnedByBarcode.ShouldNotBeNull().Id.ShouldBe(owned.ShouldBeOfType<SaveMovieResult.Saved>().Movie.Id);
        shelf.Online.Barcodes.Lookups.ShouldBe(0);
        shelf.Online.Tmdb.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_tmdb_key_the_lookup_says_so()
    {
        await using var shelf = await ShelfWithHeatsAsync();
        shelf.Online.Tmdb.FailWith = MetadataProblem.NotConfigured;

        var result = await shelf.LookupAsync("heat");

        result.Outcome.ShouldBe(LookupOutcome.Failed);
        result.Problem.ShouldBe(MetadataProblem.NotConfigured);
        result.FailedService.ShouldBe("TMDB");
    }

    [Fact]
    public async Task A_busy_barcode_database_is_reported_with_the_code_kept()
    {
        await using var shelf = await ShelfWithHeatsAsync();
        shelf.Online.Barcodes.FailWith = MetadataProblem.Busy;

        var result = await shelf.LookupAsync("786936894424");

        result.Outcome.ShouldBe(LookupOutcome.Failed);
        result.Problem.ShouldBe(MetadataProblem.Busy);
        result.FailedService.ShouldBe("UPCitemdb");
        result.Upc.ShouldBe(Upc.Parse("786936894424"));
    }
}
