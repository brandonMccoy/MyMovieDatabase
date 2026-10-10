using MMDb.Application.Comics;
using MMDb.Application.Vault;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Wishlist;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>The vault follows the wishlist: every change to it is written, and a save that grants a wish says so too.</summary>
public sealed class WishlistVaultTests : IAsyncLifetime
{
    private readonly TempFolder _vaultFolder = new();
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        _shelf.Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7"]));
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(_vaultFolder.Path, ct));
    }

    public async ValueTask DisposeAsync()
    {
        await _shelf.DisposeAsync();
        _vaultFolder.Dispose();
    }

    private IEnumerable<string> CallsAfterSetup => _shelf.Vault.Calls.Skip(1);

    private static WantedComic Wanted(ComicDetails comic) =>
        new() { Series = comic.Series, SeriesYear = comic.SeriesYear, Number = comic.Number, MetronId = comic.MetronId, MetronSeriesId = comic.MetronSeriesId };

    [Fact]
    public async Task Wanting_and_dropping_a_film_writes_the_wishlist()
    {
        var item = await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMovieAsync(TestMovies.Wanted(TestMovies.Titled(" "), MediaFormat.Dvd), ct));
        await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveMovieAsync(Guid.NewGuid(), ct));

        CallsAfterSetup.ShouldBe(["wishlist (1 wanted)"], "a refused wish and a removal of nothing change nothing");
        _shelf.VaultStatus.NoteCount.ShouldBe(1);

        await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveMovieAsync(item.Id, ct));

        CallsAfterSetup.ShouldBe(["wishlist (1 wanted)", "wishlist (0 wanted)"]);
    }

    [Fact]
    public async Task Changing_the_wanted_format_writes_the_wishlist_once()
    {
        var item = await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        await _shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(item.Id, MediaFormat.BluRay, ct));
        await _shelf.Wishlist((wishlist, ct) => wishlist.ChangeFormatAsync(item.Id, MediaFormat.BluRay, ct));

        CallsAfterSetup.ShouldBe(["wishlist (1 wanted)", "wishlist (1 wanted)"], "the same format again changes nothing");
    }

    [Fact]
    public async Task A_film_bought_on_the_wanted_format_is_written_and_then_the_wishlist()
    {
        await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        CallsAfterSetup.ShouldBe(["wishlist (1 wanted)", "write Heat", "write Heat", "wishlist (0 wanted)"]);
    }

    [Fact]
    public async Task Comics_wanted_bought_and_dropped_write_the_wishlist()
    {
        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("6"));
        var before = _shelf.Vault.Calls.Count;

        await _shelf.Wishlist((wishlist, ct) => wishlist.WantMissingAsync(1234, "2", "5", ct));
        var issue = (await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(Wanted(TestComics.Saga7), ct))).ShouldBeOfType<WantComicResult.Saved>().Comic;
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        await _shelf.AddComicAsync(TestComics.Daredevil181);
        await _shelf.Wishlist((wishlist, ct) => wishlist.RemoveComicAsync(issue.Id, ct));

        _shelf.Vault.Calls.Skip(before).ShouldBe(
        [
            "wishlist (4 wanted)",
            "wishlist (5 wanted)",
            "write Saga #3",
            "wishlist (4 wanted)",
            "write Daredevil #181",
            "wishlist (3 wanted)",
        ]);
    }

    [Fact]
    public async Task A_rebuild_gives_the_writer_the_wishlist_too()
    {
        await _shelf.WantAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);
        await _shelf.AddAsync(TestMovies.Titled("Alien", 1979), MediaFormat.Dvd);

        await _shelf.VaultAsync((sync, ct) => sync.RebuildAsync(ct));

        _shelf.VaultStatus.NoteCount.ShouldBe(2, "a film and a wish");
    }
}
