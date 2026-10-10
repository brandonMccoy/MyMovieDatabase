using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;
using MMDb.Infrastructure.Vault;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>The wishlist in the vault: a note per wanted film and comic in <c>Wishlist/</c>, the index and the Bases view.</summary>
public sealed class WishlistVaultTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _vault = new();
    private readonly TempFolder _covers = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FileVaultWriter _writer;

    public WishlistVaultTests() => _writer = new(_covers.Path, _clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folder => _vault.Path;

    public void Dispose()
    {
        _vault.Dispose();
        _covers.Dispose();
    }

    private static MovieDetails Dune => new() { Title = "Dune", Year = 2021, Directors = ["Denis Villeneuve"], TmdbId = 438631, TrailerYouTubeKey = "8g18jFHCLXk" };

    private WishlistMovie Wish(MovieDetails film, MediaFormat format = MediaFormat.Uhd4K) => WishlistMovie.Create(TestMovies.Wanted(film, format), _clock.GetUtcNow());

    private WishlistComic Issue(string number) => WishlistComic.Create(
        new WantedComic { Series = "Daredevil", SeriesYear = 1964, Publisher = "Marvel", MetronSeriesId = 77, Number = number, FromMissing = true }, _clock.GetUtcNow());

    private WishlistComic Trade => WishlistComic.Create(
        new WantedComic { Format = ComicFormat.TradePaperback, Title = "Saga, Volume One", Publisher = "Image Comics", Isbn = "9781607066019" }, _clock.GetUtcNow());

    private static VaultContent Content(IEnumerable<WishlistMovie> films, IEnumerable<WishlistComic>? comics = null, params Movie[] owned) =>
        new([.. owned.Select(m => m.Listing)], [], []) { WantedMovies = [.. films], WantedComics = [.. comics ?? []] };

    private Task WriteAsync(VaultContent content) => _writer.WriteWishlistAsync(Folder, content, Ct);

    private string Read(string relative) => File.ReadAllText(_vault[relative]);

    private bool Exists(string relative) => File.Exists(_vault[relative]);

    private string[] Notes() => Directory.Exists(_vault["Wishlist"])
        ? [.. Directory.GetFiles(_vault["Wishlist"]).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)]
        : [];

    private static string Scrub(string text, params ProductBase[] items) =>
        items.Aggregate(text, (t, i) => t.Replace(i.Id.ToString("D"), "{mmdb_id}", StringComparison.Ordinal).Replace(i.Id.ToString("N"), "{id}", StringComparison.Ordinal));

    private static void Verify(string text, string extension = "md", [CallerMemberName] string test = "") =>
        Snapshot.Match(text, $"{nameof(WishlistVaultTests)}.{test}", extension);

    [Fact]
    public async Task A_wanted_film_gets_a_note_saying_what_is_wanted()
    {
        var dune = Wish(Dune);
        File.WriteAllText(_covers[$"{dune.Id:N}-dune.jpg"], "image of dune");
        dune.UseCover($"{dune.Id:N}-dune.jpg");
        var owned = Movie.Create(Dune, [new(MediaFormat.Dvd), new(MediaFormat.Digital)], _clock.GetUtcNow());

        await WriteAsync(Content([dune], owned: owned));

        Verify(Scrub(Read("Wishlist/Dune (2021) (wanted).md"), dune));
        File.Exists(_vault[$"Attachments/covers/{dune.Id:N}-dune.jpg"]).ShouldBeTrue();
    }

    [Fact]
    public async Task Each_missing_issue_wanted_gets_a_note()
    {
        var issue = Issue("159");

        await WriteAsync(Content([], [issue, Issue("160")]));

        Notes().ShouldBe(["Daredevil 159 (wanted).md", "Daredevil 160 (wanted).md"]);
        Verify(Scrub(Read("Wishlist/Daredevil 159 (wanted).md"), issue));
    }

    [Fact]
    public async Task A_wanted_book_gets_a_note_with_its_isbn()
    {
        var trade = Trade;

        await WriteAsync(Content([], [trade]));

        var note = Read("Wishlist/Saga, Volume One (wanted).md");
        note.ShouldContain("wishlist: comic\n");
        note.ShouldContain("format: trade paperback\n");
        note.ShouldContain("isbn: \"9781607066019\"\n");
        note.ShouldContain("# Saga, Volume One\n");
        note.ShouldContain("**Wanted** Image Comics · trade paperback\n");
    }

    [Fact]
    public async Task The_index_lists_the_wishlist_apart_from_what_is_owned()
    {
        var owned = Movie.Create(Dune, [new(MediaFormat.Dvd)], _clock.GetUtcNow());
        await _writer.WriteMovieAsync(Folder, owned, Content([], owned: owned), Ct);
        var heat = Wish(TestMovies.HeatOnTmdb, MediaFormat.BluRay);

        await WriteAsync(Content([Wish(Dune), heat], [Issue("159"), Trade], owned));

        Verify(Read("_Index.md"));
    }

    [Fact]
    public async Task The_wishlist_base_has_cover_film_and_comic_views()
    {
        await WriteAsync(Content([]));

        Verify(Read("Wishlist.base"), "yaml");
    }

    [Fact]
    public async Task A_wish_granted_or_dropped_loses_its_note_and_cover()
    {
        var dune = Wish(Dune);
        var heat = Wish(TestMovies.HeatOnTmdb);
        File.WriteAllText(_covers[$"{dune.Id:N}-dune.jpg"], "image of dune");
        dune.UseCover($"{dune.Id:N}-dune.jpg");
        await WriteAsync(Content([dune, heat]));
        Notes().ShouldBe(["Dune (2021) (wanted).md", "Heat (1995) (wanted).md"]);

        await WriteAsync(Content([heat]));

        Notes().ShouldBe(["Heat (1995) (wanted).md"]);
        File.Exists(_vault[$"Attachments/covers/{dune.Id:N}-dune.jpg"]).ShouldBeFalse();
        Read("_Index.md").ShouldNotContain("Dune");
    }

    [Fact]
    public async Task What_was_written_under_my_notes_is_kept_and_goes_to_the_trash_with_a_granted_wish()
    {
        var dune = Wish(Dune);
        await WriteAsync(Content([dune]));
        File.AppendAllText(_vault["Wishlist/Dune (2021) (wanted).md"], "Wait for the steelbook.\n");

        dune.Want(TestMovies.Wanted(Dune, MediaFormat.BluRay), _clock.GetUtcNow());
        await WriteAsync(Content([dune]));

        var note = Read("Wishlist/Dune (2021) (wanted).md");
        note.ShouldContain("wanted_format: Blu-ray\n");
        note.ShouldEndWith("## My notes\nWait for the steelbook.\n");

        await WriteAsync(Content([]));

        Notes().ShouldBeEmpty();
        Read(".trash/Dune (2021) (wanted).md").ShouldEndWith("Wait for the steelbook.\n");
    }

    [Fact]
    public async Task A_full_sync_writes_the_wishlist_and_keeps_its_notes()
    {
        var dune = Wish(Dune);
        var run = Issue("159");
        var owned = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());
        var content = Content([dune], [run], owned);

        await _writer.SyncAsync(Folder, content, [owned], Ct);
        await _writer.SyncAsync(Folder, content, [owned], Ct);

        Notes().ShouldBe(["Daredevil 159 (wanted).md", "Dune (2021) (wanted).md"]);
        Exists("Movies/Heat (1995).md").ShouldBeTrue();
        _writer.NoteOf(Folder, dune.Id).ShouldBe("Wishlist/Dune (2021) (wanted).md");

        await _writer.SyncAsync(Folder, Content([], [], owned), [owned], Ct);

        Notes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_note_of_the_users_own_in_the_wishlist_folder_is_left_alone()
    {
        Directory.CreateDirectory(_vault["Wishlist"]);
        File.WriteAllText(_vault["Wishlist/Dune (2021) (wanted).md"], "My own list of Dune editions.\n");

        await WriteAsync(Content([Wish(Dune)]));
        Notes().ShouldBe(["Dune (2021) (wanted) (2).md", "Dune (2021) (wanted).md"]);
        await WriteAsync(Content([]));

        Read("Wishlist/Dune (2021) (wanted).md").ShouldBe("My own list of Dune editions.\n");
        Notes().ShouldBe(["Dune (2021) (wanted).md"]);
    }
}
