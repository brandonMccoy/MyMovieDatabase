using MMDb.Application.Movies;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Choosing the vault folder, and every change to the collection reaching the vault.</summary>
public sealed class VaultSyncTests : IAsyncLifetime
{
    private readonly TempFolder _vaultFolder = new();
    private TestShelf _shelf = null!;

    private string Folder => _vaultFolder.Path;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        await _shelf.DisposeAsync();
        _vaultFolder.Dispose();
    }

    private Task<bool> SetFolderAsync(string folder) => _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(folder, ct));

    [Fact]
    public async Task Without_a_vault_folder_saving_writes_nothing()
    {
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        _shelf.Vault.Calls.ShouldBeEmpty();
        _shelf.VaultStatus.State.ShouldBe(VaultState.NotSetUp);
    }

    [Fact]
    public async Task Choosing_a_folder_remembers_it_and_writes_the_whole_collection()
    {
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.AddAsync(TestMovies.Titled("Alien", 1979), MediaFormat.BluRay);
        _shelf.Clock.Advance(TimeSpan.FromMinutes(5));

        (await SetFolderAsync(Folder)).ShouldBeTrue();

        _shelf.Vault.Calls.ShouldBe([$"sync {Folder} (2 films)"]);
        _shelf.VaultStatus.ShouldBe(new VaultStatus
        {
            State = VaultState.Synced,
            Folder = Folder,
            LastSyncAt = _shelf.Clock.GetUtcNow(),
            NoteCount = 2,
        });
        (await _shelf.Settings.GetAsync("vault.folder", TestContext.Current.CancellationToken)).ShouldBe(Folder);
    }

    [Fact]
    public async Task A_trailing_separator_is_dropped_from_the_folder()
    {
        await SetFolderAsync(Folder + Path.DirectorySeparatorChar);

        _shelf.VaultStatus.Folder.ShouldBe(Folder);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relative")]
    public async Task A_folder_that_isnt_there_is_turned_down(string kind)
    {
        var folder = kind == "missing" ? Path.Combine(Folder, "gone") : "Documents";

        (await SetFolderAsync(folder)).ShouldBeFalse();

        _shelf.VaultStatus.State.ShouldBe(VaultState.NotSetUp);
        _shelf.Vault.Calls.ShouldBeEmpty();
        (await _shelf.Settings.GetAsync("vault.folder", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task At_startup_the_folder_chosen_last_time_is_synced()
    {
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.Settings.SetAsync("vault.folder", Folder, TestContext.Current.CancellationToken);

        await _shelf.VaultAsync((sync, ct) => sync.StartAsync(ct));

        _shelf.Vault.Calls.ShouldBe([$"sync {Folder} (1 films)"]);
        _shelf.VaultStatus.State.ShouldBe(VaultState.Synced);
    }

    [Fact]
    public async Task At_startup_with_no_folder_chosen_nothing_is_written()
    {
        await _shelf.VaultAsync((sync, ct) => sync.StartAsync(ct));

        _shelf.Vault.Calls.ShouldBeEmpty();
        _shelf.VaultStatus.State.ShouldBe(VaultState.NotSetUp);
    }

    [Fact]
    public async Task Adding_editing_and_deleting_a_film_update_its_note()
    {
        await SetFolderAsync(Folder);

        var heat = await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.Movies((catalog, ct) => catalog.UpdateAsync(heat.Id, heat.Version, TestMovies.Heat with { Rating = "R" }, TestMovies.On(MediaFormat.Dvd), ct));
        await _shelf.Movies((catalog, ct) => catalog.DeleteAsync(heat.Id, ct));

        _shelf.Vault.Calls.ShouldBe([$"sync {Folder} (0 films)", "write Heat", "write Heat", "remove Movies/Heat (1995).md"]);
        _shelf.VaultStatus.NoteCount.ShouldBe(0);
    }

    [Fact]
    public async Task Each_write_counts_the_notes_in_the_vault()
    {
        await SetFolderAsync(Folder);

        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.AddAsync(TestMovies.Titled("Alien", 1979), MediaFormat.BluRay);

        _shelf.VaultStatus.NoteCount.ShouldBe(2);
    }

    [Fact]
    public async Task Adding_a_format_to_an_owned_film_rewrites_its_note()
    {
        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        await SetFolderAsync(Folder);

        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K);

        _shelf.Vault.Calls.ShouldBe([$"sync {Folder} (1 films)", "write Heat"]);
    }

    [Fact]
    public async Task A_film_already_owned_on_that_format_writes_nothing()
    {
        await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        await SetFolderAsync(Folder);

        var result = await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), ct));

        result.ShouldBeOfType<SaveMovieResult.AlreadyOwned>();
        _shelf.Vault.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_write_doesnt_fail_the_save_and_shows_in_the_status()
    {
        await SetFolderAsync(Folder);
        _shelf.Vault.FailWith = VaultProblem.CannotWrite;

        var heat = await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        (await _shelf.GetAsync(heat.Id)).ShouldNotBeNull();
        _shelf.VaultStatus.State.ShouldBe(VaultState.Failed);
        _shelf.VaultStatus.Problem.ShouldBe(VaultProblem.CannotWrite);
        _shelf.VaultStatus.Detail.ShouldBe("Access denied");
        _shelf.VaultStatus.Folder.ShouldBe(Folder);
    }

    [Fact]
    public async Task After_a_failure_the_next_change_catches_up_on_everything()
    {
        await SetFolderAsync(Folder);
        _shelf.Vault.FailWith = VaultProblem.FolderMissing;
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        _shelf.Vault.FailWith = null;

        await _shelf.AddAsync(TestMovies.Titled("Alien", 1979), MediaFormat.BluRay);

        _shelf.Vault.Calls.ShouldBe([$"sync {Folder} (0 films)", $"sync {Folder} (2 films)"]);
        _shelf.VaultStatus.State.ShouldBe(VaultState.Synced);
        _shelf.VaultStatus.Problem.ShouldBeNull();
    }

    [Fact]
    public async Task Rebuild_writes_the_whole_collection_again()
    {
        await SetFolderAsync(Folder);
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        await _shelf.VaultAsync((sync, ct) => sync.RebuildAsync(ct));

        _shelf.Vault.Calls[^1].ShouldBe($"sync {Folder} (1 films)");
    }

    [Fact]
    public async Task Rebuild_without_a_folder_does_nothing()
    {
        await _shelf.VaultAsync((sync, ct) => sync.RebuildAsync(ct));

        _shelf.Vault.Calls.ShouldBeEmpty();
    }
}
