using Microsoft.Extensions.DependencyInjection;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Keys;
using MMDb.Infrastructure;
using MMDb.Infrastructure.Metadata;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The keys for the online databases as Settings sets them on the PC: checked, kept, and used from then on.</summary>
public sealed class KeysApiTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeOnline _online = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    /// <summary>The PC as it starts: with the keys it was configured with, then the ones saved in Settings.</summary>
    private ServiceProvider StartPc(string? tmdb = null, string? metron = null)
    {
        var settings = new ShelfSettings
        {
            ConnectionString = _sqlite.ConnectionString,
            CoverDirectory = Path.GetTempPath(),
            Tmdb = new TmdbSettings { ApiKey = tmdb },
            Metron = new MetronSettings { Token = metron },
        };
        var services = _online.ReplaceIn(new ServiceCollection().AddShelf(settings, new Uri("https://covers.test/"))).BuildServiceProvider();
        services.GetRequiredService<IKeysApi>().Load();
        return services;
    }

    private static IKeysApi Keys(ServiceProvider pc) => pc.GetRequiredService<IKeysApi>();

    /// <summary>The keys the lookups go out with.</summary>
    private static (string? Tmdb, string? Metron) InUse(ServiceProvider pc) =>
        (pc.GetRequiredService<TmdbSettings>().ApiKey, pc.GetRequiredService<MetronSettings>().Token);

    [Fact]
    public async Task With_no_keys_both_services_are_not_set_and_nothing_is_asked()
    {
        await using var pc = StartPc();

        await Keys(pc).CheckAsync(Ct);

        Keys(pc).Keys.ShouldBe([new(OnlineService.Tmdb, KeyState.NotSet), new(OnlineService.Metron, KeyState.NotSet)]);
        _online.Keys.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_the_service_takes_is_used_at_once_and_kept()
    {
        await using var pc = StartPc();
        var changes = 0;
        Keys(pc).Changed += () => changes++;

        var saved = await Keys(pc).SaveAsync(OnlineService.Tmdb, "  tmdb-key-3f9a ", Ct);

        saved.ShouldBeTrue();
        Keys(pc).Keys[0].ShouldBe(new(OnlineService.Tmdb, KeyState.Connected, "3f9a"));
        InUse(pc).Tmdb.ShouldBe("tmdb-key-3f9a");
        _online.KeyStore.Saved.ShouldBe(new SavedKeys("tmdb-key-3f9a", null));
        _online.Keys.Asked.ShouldBe(["TMDB tmdb-key-3f9a"]);
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task A_key_the_service_turns_down_is_not_kept_and_the_one_before_stays()
    {
        _online.KeyStore.Saved = new("good-tmdb-key", "metron-token");
        _online.Keys.Rejected.Add("mistyped-key");
        await using var pc = StartPc();

        var saved = await Keys(pc).SaveAsync(OnlineService.Tmdb, "mistyped-key", Ct);

        saved.ShouldBeFalse();
        InUse(pc).ShouldBe(("good-tmdb-key", "metron-token"));
        _online.KeyStore.Saved.ShouldBe(new SavedKeys("good-tmdb-key", "metron-token"));
    }

    [Fact]
    public async Task A_key_that_cant_be_checked_is_kept_and_says_so()
    {
        _online.Keys.Offline = true;
        await using var pc = StartPc();

        var saved = await Keys(pc).SaveAsync(OnlineService.Metron, "metron-token-77", Ct);

        saved.ShouldBeTrue();
        Keys(pc).Keys[1].ShouldBe(new(OnlineService.Metron, KeyState.NotChecked, "n-77"));
        _online.KeyStore.Saved.ShouldBe(new SavedKeys(null, "metron-token-77"));
    }

    [Fact]
    public async Task Saving_one_key_keeps_the_other()
    {
        _online.KeyStore.Saved = new("tmdb-key-0001", null);
        await using var pc = StartPc();

        await Keys(pc).SaveAsync(OnlineService.Metron, "metron-token-0002", Ct);

        _online.KeyStore.Saved.ShouldBe(new SavedKeys("tmdb-key-0001", "metron-token-0002"));
        InUse(pc).ShouldBe(("tmdb-key-0001", "metron-token-0002"));
    }

    [Fact]
    public async Task Saved_keys_are_used_after_a_restart()
    {
        await using (var before = StartPc())
        {
            await Keys(before).SaveAsync(OnlineService.Tmdb, "tmdb-key-0001", Ct);
        }

        await using var pc = StartPc();

        InUse(pc).ShouldBe(("tmdb-key-0001", null));
        Keys(pc).Keys[0].ShouldBe(new(OnlineService.Tmdb, KeyState.NotChecked, "0001"));
    }

    [Fact]
    public async Task Until_something_is_saved_the_keys_the_pc_was_started_with_apply()
    {
        await using var pc = StartPc(tmdb: "configured-tmdb", metron: "configured-metron");

        InUse(pc).ShouldBe(("configured-tmdb", "configured-metron"));
        Keys(pc).Keys.Select(k => k.Ending).ShouldBe(["tmdb", "tron"]);
        _online.KeyStore.Saved.ShouldBeNull();
    }

    [Fact]
    public async Task What_is_saved_in_settings_wins_over_what_the_pc_was_started_with()
    {
        await using (var before = StartPc(tmdb: "configured-tmdb", metron: "configured-metron"))
        {
            await Keys(before).SaveAsync(OnlineService.Tmdb, "typed-tmdb-key", Ct);
            Keys(before).Remove(OnlineService.Metron);
        }

        await using var pc = StartPc(tmdb: "configured-tmdb", metron: "configured-metron");

        InUse(pc).ShouldBe(("typed-tmdb-key", null));
    }

    [Fact]
    public async Task A_removed_key_is_gone_at_once_and_stays_gone()
    {
        _online.KeyStore.Saved = new("tmdb-key-0001", "metron-token-0002");
        await using var pc = StartPc();
        var changes = 0;
        Keys(pc).Changed += () => changes++;

        Keys(pc).Remove(OnlineService.Tmdb);

        Keys(pc).Keys[0].ShouldBe(new(OnlineService.Tmdb, KeyState.NotSet));
        InUse(pc).ShouldBe((null, "metron-token-0002"));
        _online.KeyStore.Saved.ShouldBe(new SavedKeys(null, "metron-token-0002"));
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task Checking_asks_each_service_about_its_key()
    {
        _online.KeyStore.Saved = new("tmdb-key-0001", "metron-token-0002");
        _online.Keys.Rejected.Add("metron-token-0002");
        await using var pc = StartPc();

        await Keys(pc).CheckAsync(Ct);

        Keys(pc).Keys.Select(k => k.State).ShouldBe([KeyState.Connected, KeyState.Rejected]);
        _online.Keys.Asked.ShouldBe(["TMDB tmdb-key-0001", "Metron metron-token-0002"], ignoreOrder: true);
        InUse(pc).ShouldBe(("tmdb-key-0001", "metron-token-0002"), "a key turned down later is still the key until it is changed");
    }

    [Fact]
    public async Task A_first_check_shows_as_checking_until_the_service_answers()
    {
        _online.KeyStore.Saved = new("tmdb-key-0001", null);
        _online.Keys.Hold = new();
        await using var pc = StartPc();

        var check = Keys(pc).CheckAsync(Ct);

        Keys(pc).Keys[0].State.ShouldBe(KeyState.Checking);
        _online.Keys.Hold.SetResult();
        await check;
        Keys(pc).Keys[0].State.ShouldBe(KeyState.Connected);
    }

    [Fact]
    public async Task A_later_check_leaves_the_last_answer_showing_until_the_new_one_is_in()
    {
        _online.KeyStore.Saved = new("tmdb-key-0001", null);
        await using var pc = StartPc();
        await Keys(pc).CheckAsync(Ct);
        _online.Keys.Hold = new();
        _online.Keys.Rejected.Add("tmdb-key-0001");

        var check = Keys(pc).CheckAsync(Ct);

        Keys(pc).Keys[0].State.ShouldBe(KeyState.Connected);
        _online.Keys.Hold.SetResult();
        await check;
        Keys(pc).Keys[0].State.ShouldBe(KeyState.Rejected);
    }

    [Fact]
    public async Task An_answer_about_a_key_changed_meanwhile_is_dropped()
    {
        _online.KeyStore.Saved = new("old-tmdb-key", null);
        _online.Keys.Rejected.Add("old-tmdb-key");
        await using var pc = StartPc();
        var hold = _online.Keys.Hold = new();
        var check = Keys(pc).CheckAsync(Ct);

        _online.Keys.Hold = null;
        await Keys(pc).SaveAsync(OnlineService.Tmdb, "new-tmdb-key", Ct);
        hold.SetResult();
        await check;

        Keys(pc).Keys[0].ShouldBe(new(OnlineService.Tmdb, KeyState.Connected, "-key"));
    }

    [Fact]
    public async Task A_short_key_shows_none_of_itself()
    {
        await using var pc = StartPc();

        await Keys(pc).SaveAsync(OnlineService.Tmdb, "12345678", Ct);

        Keys(pc).Keys[0].ShouldBe(new(OnlineService.Tmdb, KeyState.Connected));
    }
}
