using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Vault;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>Comics in the vault: issue and trade notes, a note per series, and folders that follow the collection.</summary>
public sealed class ComicVaultTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _vault = new();
    private readonly TempFolder _covers = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FileVaultWriter _writer;

    public ComicVaultTests() => _writer = new(_covers.Path, _clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folder => _vault.Path;

    public void Dispose()
    {
        _vault.Dispose();
        _covers.Dispose();
    }

    private static ComicSeries Saga => ComicSeries.Create(1234, Start, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]));

    private Comic Copy(ComicDetails details)
    {
        _clock.Advance(TimeSpan.FromMinutes(1));
        return Comic.Create(details, _clock.GetUtcNow());
    }

    private Comic WithCover(Comic comic, string name = "saga-7.jpg")
    {
        var cover = $"{comic.Id:N}-{name}";
        File.WriteAllText(_covers[cover], "image of " + name);
        comic.UseCover(cover);
        return comic;
    }

    private static VaultContent Shelf(IEnumerable<Comic> comics, IEnumerable<ComicSeries>? series = null, IEnumerable<Movie>? movies = null) =>
        new([.. movies ?? []], [.. comics], [.. series ?? []]);

    private Task SyncAsync(params Comic[] comics) => _writer.SyncAsync(Folder, Shelf(comics, [Saga]), Ct);

    private string Read(string relative) => File.ReadAllText(_vault[relative]);

    private bool Exists(string relative) => File.Exists(_vault[relative]) || Directory.Exists(_vault[relative]);

    private static string Scrub(string text, params Comic[] comics) =>
        comics.Aggregate(text, (t, c) => t.Replace(c.Id.ToString("D"), "{mmdb_id}", StringComparison.Ordinal).Replace(c.Id.ToString("N"), "{id}", StringComparison.Ordinal));

    private static void Verify(string text, string extension = "md", [CallerMemberName] string test = "") =>
        Snapshot.Match(text, $"{nameof(ComicVaultTests)}.{test}", extension);

    [Fact]
    public async Task An_issue_from_metron_gets_a_full_note()
    {
        var saga = WithCover(Copy(TestComics.Saga7 with { IsKeyIssue = true, KeyNote = "1st The Will" }));

        await _writer.WriteComicAsync(Folder, saga, Shelf([saga], [Saga]), Ct);

        Verify(Scrub(Read("Comics/Saga (2012)/Saga 7.md"), saga));
    }

    [Fact]
    public async Task A_trade_gets_a_note_on_its_own_shelf()
    {
        var trade = Copy(TestComics.SagaVolume1);

        await SyncAsync(trade);

        Verify(Scrub(Read("Comics/Trades & HCs/Saga, Volume One.md"), trade));
    }

    [Fact]
    public async Task The_series_note_lists_issues_owned_and_missing()
    {
        var first = Copy(TestComics.SagaIssue("1") with { IsKeyIssue = true, KeyNote = "First issue" });
        var second = Copy(TestComics.SagaIssue("2"));
        var regular = Copy(TestComics.Saga7);
        var variant = Copy(TestComics.Saga7 with { Variant = "Cover B", Barcode = "70985301744800721" });
        var reprint = Copy(TestComics.SagaIssue("3") with { Printing = 2 });

        await SyncAsync(first, second, regular, variant, reprint);

        Verify(Read("Comics/Saga (2012)/_Series.md"));
    }

    [Fact]
    public async Task The_index_has_a_line_per_series_and_per_trade()
    {
        var heat = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.BluRay), Start);
        var comics = new[]
        {
            Copy(TestComics.SagaIssue("1")),
            Copy(TestComics.SagaIssue("2")),
            Copy(TestComics.Saga7),
            Copy(TestComics.Daredevil181),
            Copy(TestComics.SagaVolume1),
        };

        await _writer.SyncAsync(Folder, Shelf(comics, [Saga], [heat]), Ct);

        Verify(Read("_Index.md"));
    }

    [Fact]
    public async Task The_comics_base_has_issue_series_key_and_trade_views()
    {
        await SyncAsync();

        Verify(Read("Comics.base"), "yaml");
    }

    [Fact]
    public async Task Each_variant_and_printing_owned_has_its_own_note()
    {
        var regular = Copy(TestComics.Saga7);
        var variant = Copy(TestComics.Saga7 with { Variant = "Cover B" });
        var reprint = Copy(TestComics.Saga7 with { Printing = 2 });

        await SyncAsync(regular, variant, reprint);

        _writer.NoteOf(Folder, regular.Id).ShouldBe("Comics/Saga (2012)/Saga 7.md");
        _writer.NoteOf(Folder, variant.Id).ShouldBe("Comics/Saga (2012)/Saga 7 (Cover B).md");
        _writer.NoteOf(Folder, reprint.Id).ShouldBe("Comics/Saga (2012)/Saga 7 (2nd printing).md");
    }

    [Fact]
    public async Task Names_leave_out_what_links_cant_hold()
    {
        var comic = Copy(TestComics.Issue("Batman: Year One", "#1", 1987));

        await SyncAsync(comic);

        _writer.NoteOf(Folder, comic.Id).ShouldBe("Comics/Batman - Year One (1987)/Batman - Year One 1.md");
        Read("Comics/Batman - Year One (1987)/Batman - Year One 1.md").ShouldContain("# Batman: Year One \\#1\n");
    }

    [Fact]
    public async Task Moving_an_issue_to_another_series_keeps_my_notes_and_tidies_the_old_folder()
    {
        var comic = Copy(TestComics.Issue("Sága", "7", 2012));
        await SyncAsync(comic);
        File.AppendAllText(_vault["Comics/Sága (2012)/Sága 7.md"], "Signed at a con.\n");

        comic.Update(TestComics.Issue("Saga", "7", 2012), _clock.GetUtcNow());
        await _writer.WriteComicAsync(Folder, comic, Shelf([comic]), Ct);

        Read("Comics/Saga (2012)/Saga 7.md").ShouldEndWith("## My notes\nSigned at a con.\n");
        Exists("Comics/Sága (2012)").ShouldBeFalse();
        Exists("Comics/Saga (2012)/_Series.md").ShouldBeTrue();
    }

    [Fact]
    public async Task Removing_the_last_issue_of_a_series_removes_its_series_note_and_folder()
    {
        var comic = Copy(TestComics.Saga7);
        await SyncAsync(comic);

        await _writer.RemoveAsync(Folder, comic.Id, VaultContent.Empty, Ct);

        Exists("Comics/Saga (2012)").ShouldBeFalse();
        Read("_Index.md").ShouldNotContain("Saga");
    }

    [Fact]
    public async Task A_folder_with_the_users_own_files_stays()
    {
        var comic = Copy(TestComics.Saga7);
        await SyncAsync(comic);
        File.WriteAllText(_vault["Comics/Saga (2012)/Reading order.md"], "Mine.");

        await _writer.RemoveAsync(Folder, comic.Id, VaultContent.Empty, Ct);

        Read("Comics/Saga (2012)/Reading order.md").ShouldBe("Mine.");
        Exists("Comics/Saga (2012)/_Series.md").ShouldBeFalse();
        Exists("Comics/Saga (2012)/Saga 7.md").ShouldBeFalse();
    }

    [Fact]
    public async Task A_series_note_of_the_users_own_is_left_alone()
    {
        Directory.CreateDirectory(_vault["Comics/Saga (2012)"]);
        File.WriteAllText(_vault["Comics/Saga (2012)/_Series.md"], "# My Saga notes");
        var comic = Copy(TestComics.Saga7);

        await SyncAsync(comic);
        await _writer.RemoveAsync(Folder, comic.Id, VaultContent.Empty, Ct);

        Read("Comics/Saga (2012)/_Series.md").ShouldBe("# My Saga notes");
    }

    [Fact]
    public async Task Rebuilding_removes_comics_no_longer_owned_and_keeps_the_users_notes()
    {
        var kept = Copy(TestComics.Saga7);
        var sold = Copy(TestComics.Daredevil181);
        await SyncAsync(kept, sold);
        File.WriteAllText(_vault["Comics/Wanted.md"], "Saga #8");

        await SyncAsync(kept);

        Exists("Comics/Daredevil (1964)").ShouldBeFalse();
        Exists("Comics/Saga (2012)/Saga 7.md").ShouldBeTrue();
        Read("Comics/Wanted.md").ShouldBe("Saga #8");
    }

    [Fact]
    public async Task A_comic_with_my_notes_goes_to_the_trash_when_removed()
    {
        var comic = Copy(TestComics.Daredevil181);
        await SyncAsync(comic);
        File.AppendAllText(_vault["Comics/Daredevil (1964)/Daredevil 181.md"], "Graded 9.4.\n");

        await _writer.RemoveAsync(Folder, comic.Id, VaultContent.Empty, Ct);

        Read(".trash/Daredevil 181.md").ShouldEndWith("Graded 9.4.\n");
    }

    [Fact]
    public async Task Comic_covers_are_copied_and_removed_with_the_comic()
    {
        var comic = WithCover(Copy(TestComics.Saga7));
        await SyncAsync(comic);
        Exists($"Attachments/covers/{comic.Id:N}-saga-7.jpg").ShouldBeTrue();

        await SyncAsync();

        Exists($"Attachments/covers/{comic.Id:N}-saga-7.jpg").ShouldBeFalse();
    }

    [Fact]
    public async Task The_last_trade_removed_takes_its_shelf_folder_with_it()
    {
        var trade = Copy(TestComics.SagaVolume1);
        await SyncAsync(trade);

        await SyncAsync();

        Exists("Comics/Trades & HCs").ShouldBeFalse();
    }
}
