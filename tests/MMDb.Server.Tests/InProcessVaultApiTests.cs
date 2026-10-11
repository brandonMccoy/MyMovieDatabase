using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Vault;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The PC wiring end to end: saving through the API writes real notes into a real folder.</summary>
public sealed class InProcessVaultApiTests : IAsyncLifetime
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly TempFolder _vault = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private IVaultApi Vault => _services.GetRequiredService<IVaultApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(_clock);
        _services = new FakeOnline().ReplaceIn(services).BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
        _vault.Dispose();
    }

    private static SaveMovieRequest Heat => new() { Title = "Heat", Year = 1995, Directors = ["Michael Mann"], Formats = [new(MovieFormatKind.Dvd)] };

    [Fact]
    public async Task Choosing_a_folder_writes_the_collection_and_later_saves_follow()
    {
        var heat = (await Api.AddMovieAsync(Heat, Ct)).Movie!;

        (await Vault.SetFolderAsync(_vault.Path, Ct)).ShouldBeTrue();
        await Api.AddMovieAsync(new() { Title = "Alien", Year = 1979, Formats = [new(MovieFormatKind.BluRay)] }, Ct);
        await Api.UpdateMovieAsync(heat.Id, heat.Version, Heat with { Formats = [new(MovieFormatKind.Uhd4K), new(MovieFormatKind.Dvd)] }, Ct);

        Vault.Status.ShouldBe(new VaultStatusDto(VaultSyncState.Synced, _vault.Path, _clock.GetUtcNow(), 2));
        File.ReadAllText(_vault["Movies/Heat (1995).md"]).ShouldContain("**Owned on** 4K UHD, DVD\n");
        File.ReadAllText(_vault["_Index.md"]).ShouldContain("- [[Alien (1979)]] · Blu-ray\n");
        File.Exists(_vault["CLAUDE.md"]).ShouldBeTrue();
        File.Exists(_vault["Movies.base"]).ShouldBeTrue();
        Vault.NoteFor(heat.Id)!.Path.ShouldBe("Movies/Heat (1995).md");
    }

    [Fact]
    public async Task The_folder_is_remembered_for_the_next_start()
    {
        await Vault.SetFolderAsync(_vault.Path, Ct);
        await Api.AddMovieAsync(Heat, Ct);
        File.Delete(_vault["Movies/Heat (1995).md"]);

        await using var restarted = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(_clock)
            .BuildServiceProvider();
        var vault = restarted.GetRequiredService<IVaultApi>();
        await vault.StartAsync(Ct);

        vault.Status.State.ShouldBe(VaultSyncState.Synced);
        File.Exists(_vault["Movies/Heat (1995).md"]).ShouldBeTrue();
    }

    [Fact]
    public async Task Turning_the_vault_off_leaves_what_is_in_it_and_stays_off_after_a_restart()
    {
        await Vault.SetFolderAsync(_vault.Path, Ct);
        var heat = (await Api.AddMovieAsync(Heat, Ct)).Movie!;
        var before = Directory.GetFiles(_vault.Path, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);

        await Vault.SetUseAsync(false, Ct);
        await Api.AddMovieAsync(new() { Title = "Alien", Year = 1979, Formats = [new(MovieFormatKind.BluRay)] }, Ct);
        await Api.DeleteMovieAsync(heat.Id, Ct);

        await using var restarted = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(_clock)
            .BuildServiceProvider();
        var vault = restarted.GetRequiredService<IVaultApi>();
        await vault.StartAsync(Ct);

        Vault.Status.ShouldBe(new VaultStatusDto(VaultSyncState.Off));
        vault.Status.ShouldBe(new VaultStatusDto(VaultSyncState.Off));
        Vault.NoteFor(heat.Id).ShouldBeNull();
        File.Exists(_vault["Movies/Heat (1995).md"]).ShouldBeTrue();
        Directory.GetFiles(_vault.Path, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText).ShouldBe(before);
    }

    [Fact]
    public async Task Turned_back_on_the_vault_catches_up_with_what_changed_meanwhile()
    {
        await Vault.SetFolderAsync(_vault.Path, Ct);
        await Vault.SetUseAsync(false, Ct);
        await Api.AddMovieAsync(Heat, Ct);

        await Vault.SetUseAsync(true, Ct);

        Vault.Status.ShouldBe(new VaultStatusDto(VaultSyncState.Synced, _vault.Path, _clock.GetUtcNow(), 1));
        File.Exists(_vault["Movies/Heat (1995).md"]).ShouldBeTrue();
    }

    [Fact]
    public async Task Status_changes_are_announced()
    {
        var states = new List<VaultSyncState>();
        Vault.StatusChanged += () => states.Add(Vault.Status.State);

        await Vault.SetFolderAsync(_vault.Path, Ct);

        states.ShouldBe([VaultSyncState.Syncing, VaultSyncState.Synced]);
    }
}
