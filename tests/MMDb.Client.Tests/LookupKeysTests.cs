using Microsoft.Extensions.DependencyInjection;
using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Infrastructure.Metadata;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>The PC's keys for the online databases on the phone: when they arrive, where they are kept, when they go.</summary>
public sealed class LookupKeysTests : IAsyncLifetime
{
    private readonly InMemorySqlite _sqlite = new();
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PcLink Link => _phone.Link;

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _pc.HasKeys(tmdb: "tmdb-key", metron: "metron-token");
        _phone = new TestPhone(_pc);
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task Pairing_hands_the_phone_the_pcs_keys_and_they_are_kept_with_the_pairing()
    {
        var told = 0;
        Link.KeysChanged += () => told++;
        await Link.StartAsync(Ct);
        Link.Keys.ShouldBe(new LookupKeysResponse(null, null));

        await Link.PairAsync(_phone.QrText(), Ct);

        Link.Keys.ShouldBe(new LookupKeysResponse("tmdb-key", "metron-token"));
        _phone.Store.Record.ShouldNotBeNull().ShouldSatisfyAllConditions(
            r => r.TmdbApiKey.ShouldBe("tmdb-key"),
            r => r.MetronToken.ShouldBe("metron-token"));
        told.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task The_keys_are_there_when_the_app_starts_again_without_the_pc()
    {
        await _phone.PairAsync();
        _phone.Transport.Down.Add(TestPc.Address);
        using var restarted = new PcLink(_phone.Store, _phone.Transport, new PhoneIdentity("Pixel 8"));

        await restarted.StartAsync(Ct);
        await restarted.CheckAsync(Ct);

        restarted.State.ShouldBe(PcLinkState.Unreachable);
        restarted.Keys.ShouldBe(new LookupKeysResponse("tmdb-key", "metron-token"));
    }

    [Fact]
    public async Task A_key_changed_on_the_pc_reaches_the_phone_the_next_time_it_checks_in()
    {
        await _phone.PairAsync();
        _pc.HasKeys(tmdb: "new-tmdb-key", metron: null);

        await Link.CheckAsync(Ct);

        Link.Keys.ShouldBe(new LookupKeysResponse("new-tmdb-key", null));
        _phone.Store.Record!.TmdbApiKey.ShouldBe("new-tmdb-key");
        _phone.Store.Record.MetronToken.ShouldBeNull();
    }

    [Fact]
    public async Task Unpairing_on_the_pc_takes_the_keys_off_the_phone()
    {
        await _phone.PairAsync();
        var device = (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem();
        await _pc.Phone.UnpairAsync(device.Id, Ct);

        await Link.CheckAsync(Ct);

        Link.State.ShouldBe(PcLinkState.Unpaired);
        Link.Keys.ShouldBe(new LookupKeysResponse(null, null));
        _phone.Store.Record.ShouldNotBeNull().ShouldSatisfyAllConditions(
            r => r.TmdbApiKey.ShouldBeNull(),
            r => r.MetronToken.ShouldBeNull());
    }

    [Fact]
    public async Task Forgetting_the_pc_drops_its_keys()
    {
        await _phone.PairAsync();

        await Link.ForgetAsync(Ct);

        Link.Keys.ShouldBe(new LookupKeysResponse(null, null));
    }

    [Fact]
    public async Task The_phones_own_lookups_use_the_pcs_keys_for_as_long_as_it_holds_them()
    {
        await using var services = new ServiceCollection()
            .AddSingleton<ILinkStore>(_phone.Store)
            .AddSingleton<IPcTransport>(_phone.Transport)
            .AddSingleton(new PhoneIdentity("Pixel 8"))
            .AddShelfPhone(FakeOnline.Settings(_sqlite), TestPc.CoverBase)
            .BuildServiceProvider();
        var link = services.GetRequiredService<PcLink>();
        var tmdb = services.GetRequiredService<TmdbSettings>();
        var metron = services.GetRequiredService<MetronSettings>();

        await services.StartShelfPhoneAsync(Ct);
        await link.StartAsync(Ct);
        tmdb.ApiKey.ShouldBeNull();

        await link.PairAsync(_phone.QrText(), Ct);

        tmdb.ApiKey.ShouldBe("tmdb-key");
        metron.Token.ShouldBe("metron-token");

        await link.ForgetAsync(Ct);

        tmdb.ApiKey.ShouldBeNull();
        metron.Token.ShouldBeNull();
    }
}
