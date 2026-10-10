using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Application.Vault;
using MMDb.Domain.Comics;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Adding, editing and deleting comic copies, with their covers, series facts and vault notes.</summary>
public sealed class ComicCatalogTests : IAsyncLifetime
{
    private static readonly SeriesFacts SagaSeries = new("Saga", 2012, "Image", ["1", "2", "3", "4", "5", "6", "7"]);

    private readonly TempFolder _vaultFolder = new();
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        await _shelf.DisposeAsync();
        _vaultFolder.Dispose();
    }

    private Task<SaveComicResult> AddAsync(ComicDetails details) => _shelf.Comics((catalog, ct) => catalog.AddAsync(details, ct));

    private Task<Comic?> GetAsync(Guid id) => _shelf.Comics((catalog, ct) => catalog.GetAsync(id, ct));

    [Fact]
    public async Task A_new_comic_is_saved_with_every_field()
    {
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        var loaded = (await GetAsync(saga.Id)).ShouldNotBeNull();
        loaded.DisplayName.ShouldBe("Saga #7");
        loaded.Barcode!.Supplement.ShouldBe("00711");
        loaded.CreatedAt.ShouldBe(TestShelf.Start);
    }

    [Fact]
    public async Task Invalid_input_is_turned_down_and_nothing_is_saved()
    {
        var result = await AddAsync(new ComicDetails { Series = "Saga" });

        result.ShouldBeOfType<SaveComicResult.Invalid>().Errors.ShouldHaveSingleItem().Field.ShouldBe(ComicFields.Number);
        (await _shelf.ListComicsAsync(new() { View = ComicView.Issues })).Comics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("barcode")]
    [InlineData("metron")]
    [InlineData("isbn")]
    public async Task The_same_copy_twice_is_turned_down(string by)
    {
        var details = by switch
        {
            "barcode" => TestComics.Saga7 with { MetronId = null },
            "metron" => TestComics.Saga7 with { Barcode = null },
            _ => TestComics.SagaVolume1,
        };
        var owned = await _shelf.AddComicAsync(details);

        var result = await AddAsync(details with { Summary = "Typed again" });

        result.ShouldBeOfType<SaveComicResult.AlreadyOwned>().Comic.Id.ShouldBe(owned.Id);
    }

    [Fact]
    public async Task Another_variant_or_printing_of_an_owned_issue_is_a_copy_of_its_own()
    {
        await _shelf.AddComicAsync(TestComics.Saga7);

        await _shelf.AddComicAsync(TestComics.Saga7 with { Variant = "Cover B", Barcode = "70985301744800721" });
        await _shelf.AddComicAsync(TestComics.Saga7 with { Printing = 2, Barcode = "70985301744800712" });

        (await _shelf.ListComicsAsync(new() { View = ComicView.Issues })).Comics.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Issues_typed_in_by_hand_can_be_added_twice()
    {
        await _shelf.AddComicAsync(TestComics.Daredevil181);

        (await AddAsync(TestComics.Daredevil181)).ShouldBeOfType<SaveComicResult.Saved>();
    }

    [Fact]
    public async Task Editing_saves_the_changes()
    {
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        var result = await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { IsKeyIssue = true, KeyNote = "1st The Will" }, ct));

        result.ShouldBeOfType<SaveComicResult.Saved>();
        (await GetAsync(saga.Id)).ShouldNotBeNull().KeyNote.ShouldBe("1st The Will");
    }

    [Fact]
    public async Task Editing_an_older_version_is_a_conflict()
    {
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);
        await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { Variant = "A" }, ct));

        var result = await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { Variant = "B" }, ct));

        result.ShouldBeOfType<SaveComicResult.Conflict>();
    }

    [Fact]
    public async Task Editing_or_deleting_a_comic_that_is_gone()
    {
        (await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(Guid.NewGuid(), 1, TestComics.Saga7, ct))).ShouldBeOfType<SaveComicResult.NotFound>();
        (await _shelf.Comics((catalog, ct) => catalog.DeleteAsync(Guid.NewGuid(), ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task The_cover_is_downloaded_once_and_removed_with_the_comic()
    {
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);
        var cover = saga.CoverImage.ShouldNotBeNull();
        _shelf.Online.Covers.Files[cover].ShouldBe(FakeComicCovers.ImageFor(TestComics.Saga7.CoverUrl!));
        cover.ShouldStartWith($"{saga.Id:N}-");
        cover.ShouldEndWith(".jpg");

        await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { Variant = "A" }, ct));
        _shelf.Online.ComicCovers.Fetched.Count.ShouldBe(1);

        await _shelf.Comics((catalog, ct) => catalog.DeleteAsync(saga.Id, ct));
        _shelf.Online.Covers.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_new_cover_link_replaces_the_cover()
    {
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { CoverUrl = "https://static.metron.cloud/media/issue/saga-7b.png" }, ct));

        var cover = _shelf.Online.Covers.Files.Keys.ShouldHaveSingleItem();
        cover.ShouldEndWith(".png");
        cover.ShouldNotBe(saga.CoverImage);
    }

    [Fact]
    public async Task Offline_the_comic_is_saved_without_a_cover()
    {
        _shelf.Online.ComicCovers.Offline = true;

        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        saga.CoverImage.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_an_issue_fetches_its_series_once_a_week()
    {
        _shelf.Online.Metron.AddSeries(1234, SagaSeries);

        await _shelf.AddComicAsync(TestComics.SagaIssue("1"));
        await _shelf.AddComicAsync(TestComics.SagaIssue("2"));
        _shelf.Online.Metron.SeriesFetches.ShouldBe(1);

        _shelf.Clock.Advance(ComicSeries.RefreshAfter);
        await _shelf.AddComicAsync(TestComics.SagaIssue("3"));
        _shelf.Online.Metron.SeriesFetches.ShouldBe(2);
    }

    [Fact]
    public async Task Without_metron_the_issue_is_still_saved_and_written_to_the_vault()
    {
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(_vaultFolder.Path, ct));
        _shelf.Online.Metron.FailWith = MetadataProblem.Unavailable;

        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        (await GetAsync(saga.Id)).ShouldNotBeNull();
        _shelf.Vault.Calls[^1].ShouldBe("write Saga #7");
    }

    [Fact]
    public async Task Adding_editing_and_deleting_a_comic_update_its_note()
    {
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(_vaultFolder.Path, ct));

        var saga = await _shelf.AddComicAsync(TestComics.Saga7);
        await _shelf.Comics((catalog, ct) => catalog.UpdateAsync(saga.Id, saga.Version, TestComics.Saga7 with { Variant = "A" }, ct));
        await _shelf.Comics((catalog, ct) => catalog.DeleteAsync(saga.Id, ct));

        _shelf.Vault.Calls.ShouldBe([$"sync {_vaultFolder.Path} (0 films)", "write Saga #7", "write Saga #7", "remove Comics/Saga (2012)/Saga 7.md"]);
    }

    [Fact]
    public async Task The_vault_counts_films_and_comics()
    {
        await _shelf.AddAsync(TestMovies.Heat, Domain.MediaFormat.Dvd);
        await _shelf.AddComicAsync(TestComics.Saga7);

        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(_vaultFolder.Path, ct));

        _shelf.Vault.Calls.ShouldBe([$"sync {_vaultFolder.Path} (1 films, 1 comics)"]);
        _shelf.VaultStatus.ShouldSatisfyAllConditions(s => s.State.ShouldBe(VaultState.Synced), s => s.NoteCount.ShouldBe(2));
    }

    [Fact]
    public async Task An_issues_run_shows_its_progress_and_gaps()
    {
        _shelf.Online.Metron.AddSeries(1234, SagaSeries);
        await _shelf.AddComicAsync(TestComics.SagaIssue("2"));
        var saga = await _shelf.AddComicAsync(TestComics.Saga7);

        var run = await _shelf.Comics((catalog, ct) => catalog.RunOfAsync(saga, ct));

        run.ShouldNotBeNull().ShouldSatisfyAllConditions(
            r => r.Title.ShouldBe("Saga (2012)"),
            r => r.OwnedCount.ShouldBe(2),
            r => r.KnownCount.ShouldBe(7),
            r => r.Entries.Select(e => (e.Number, e.IsMissing)).ShouldBe([("2", false), ("3", true), ("4", true), ("5", true), ("6", true), ("7", false)]));
    }

    [Fact]
    public async Task A_trade_has_no_run()
    {
        var trade = await _shelf.AddComicAsync(TestComics.SagaVolume1);

        (await _shelf.Comics((catalog, ct) => catalog.RunOfAsync(trade, ct))).ShouldBeNull();
    }
}
