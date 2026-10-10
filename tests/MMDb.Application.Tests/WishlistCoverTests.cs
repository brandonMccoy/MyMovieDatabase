using MMDb.Application.Metadata;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Covers the wishlist could not get when something was wanted are tried for again later.</summary>
public sealed class WishlistCoverTests : IAsyncLifetime
{
    private const int Saga = 1234;

    private readonly TempFolder _vaultFolder = new();
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        _shelf.Online.Metron.AddSeries(Saga, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7"]));
        _shelf.Online.Metron.Add(TestComics.Saga7).Add(TestComics.SagaIssue("4")).Add(TestComics.SagaIssue("5"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.Saga7);
    }

    public async ValueTask DisposeAsync()
    {
        await _shelf.DisposeAsync();
        _vaultFolder.Dispose();
    }

    private Task<int> FetchMissingAsync() => _shelf.Wishlist((wishlist, ct) => wishlist.FetchMissingCoversAsync(ct));

    [Fact]
    public async Task Issues_wanted_before_metron_could_be_asked_get_their_entry_and_cover_later()
    {
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, "2", "6", ct));
        (await _shelf.WishlistAsync()).Comics.ShouldAllBe(c => c.CoverImage == null && c.MetronId == null);
        _shelf.Online.Metron.FailWith = null;
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(_vaultFolder.Path, ct));

        var got = await FetchMissingAsync();

        // Metron (the fake) lists #4 and #5 with covers; #2, #3 and #6 it doesn't list.
        got.ShouldBe(2);
        var wanted = (await _shelf.WishlistAsync()).Comics;
        wanted.Select(c => (c.Number, c.MetronId, HasCover: c.CoverImage is not null)).ShouldBe(
        [
            ("2", null, false), ("3", null, false), ("4", 4518, true), ("5", 4519, true), ("6", null, false),
        ]);
        var five = wanted.Single(c => c.Number == "5");
        _shelf.Online.Covers.Files[five.CoverImage!].ShouldBe(FakeComicCovers.ImageFor(five.CoverUrl!));
        _shelf.Vault.Calls[^1].ShouldBe("wishlist (5 wanted)");
    }

    [Fact]
    public async Task A_cover_that_could_not_be_downloaded_is_tried_for_again()
    {
        _shelf.Online.ComicCovers.Offline = true;
        _shelf.Online.Posters.Offline = true;
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, "4", null, ct));
        await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        (await FetchMissingAsync()).ShouldBe(0, "still offline");
        _shelf.Online.ComicCovers.Offline = false;
        _shelf.Online.Posters.Offline = false;

        (await FetchMissingAsync()).ShouldBe(2);

        var wishlist = await _shelf.WishlistAsync();
        wishlist.Comics.ShouldHaveSingleItem().CoverImage.ShouldNotBeNull();
        var film = wishlist.Movies.ShouldHaveSingleItem().Item;
        _shelf.Online.Covers.Files[film.CoverImage.ShouldNotBeNull()].ShouldBe(FakePosters.ImageFor("/heat.jpg"));
    }

    [Fact]
    public async Task With_every_cover_there_nothing_is_asked_or_downloaded()
    {
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, "4", "5", ct));
        await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        await _shelf.WantAsync(TestMovies.Titled("Alien", 1979), MediaFormat.Dvd);
        var fetched = _shelf.Online.ComicCovers.Fetched.Count + _shelf.Online.Posters.Fetched.Count;
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;

        (await FetchMissingAsync()).ShouldBe(0);

        (_shelf.Online.ComicCovers.Fetched.Count + _shelf.Online.Posters.Fetched.Count).ShouldBe(fetched, "a film without a poster on TMDB has none to fetch");
    }

    [Fact]
    public async Task What_still_cant_be_had_stays_as_it_is()
    {
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(Saga, "2", "3", ct));

        (await FetchMissingAsync()).ShouldBe(0);

        (await _shelf.WishlistAsync()).Comics.Select(c => (c.DisplayName, c.CoverImage)).ShouldBe([("Saga #2", null), ("Saga #3", null)]);
    }
}
