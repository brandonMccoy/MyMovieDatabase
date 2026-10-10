using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>A scanned barcode is looked up as a film or as a comic.</summary>
public sealed class ScanLookupTests : IAsyncLifetime
{
    private const string HeatUpc = "786936894424";

    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        _shelf.Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _shelf.Online.Barcodes.Add(HeatUpc, "Heat [4K UHD]");
        _shelf.Online.Metron.Add(TestComics.Saga7);
        _shelf.Online.Books.Add(TestComics.SagaVolume1);
    }

    public async ValueTask DisposeAsync() => await _shelf.DisposeAsync();

    [Fact]
    public async Task A_plain_upc_is_looked_up_as_a_film()
    {
        var result = await _shelf.ScanAsync(HeatUpc);

        result.Kind.ShouldBe(ProductKind.Movie);
        result.Comic.ShouldBeNull();
        result.Movie.ShouldNotBeNull().Matches.ShouldHaveSingleItem().Details.Title.ShouldBe("Heat");
    }

    [Fact]
    public async Task A_upc_with_an_add_on_is_looked_up_as_a_comic()
    {
        var result = await _shelf.ScanAsync(TestComics.Saga7.Barcode!);

        result.Kind.ShouldBe(ProductKind.Comic);
        result.Movie.ShouldBeNull();
        result.Comic.ShouldNotBeNull().Matches.ShouldHaveSingleItem().Details.DisplayName.ShouldBe("Saga #7");
        _shelf.Online.Barcodes.Lookups.ShouldBe(0);
    }

    [Fact]
    public async Task An_isbn_is_looked_up_as_a_trade()
    {
        var result = await _shelf.ScanAsync(TestComics.SagaVolume1.Isbn!);

        result.Kind.ShouldBe(ProductKind.Comic);
        result.Comic.ShouldNotBeNull().Matches.ShouldHaveSingleItem().Details.Title.ShouldBe("Saga, Volume One");
    }

    [Fact]
    public async Task What_the_user_says_it_is_wins_over_what_the_code_looks_like()
    {
        // A comic's UPC read without its add-on looks like a film's.
        var upcAlone = TestComics.Saga7.Barcode![..12];

        var asComic = await _shelf.ScanAsync(upcAlone, ProductKind.Comic);
        var asFilm = await _shelf.ScanAsync(TestComics.Saga7.Barcode!, ProductKind.Movie);

        asComic.Kind.ShouldBe(ProductKind.Comic);
        asComic.Comic.ShouldNotBeNull();
        asFilm.Kind.ShouldBe(ProductKind.Movie);
        asFilm.Movie.ShouldNotBeNull().Outcome.ShouldBe(LookupOutcome.NoMatches);
    }
}
