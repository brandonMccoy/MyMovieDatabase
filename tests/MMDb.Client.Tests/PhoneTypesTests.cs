using MMDb.Contracts;
using MMDb.Contracts.Types;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>The types of collection a phone shows: the ones its PC's Settings say, learnt with every sync and kept for when the PC is away.</summary>
public sealed class PhoneTypesTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ITypesApi Shown => _phone.Service<ITypesApi>();

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _phone = new TestPhone(_pc);
        await _phone.PairAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
    }

    [Fact]
    public async Task A_phone_shows_every_type_until_its_pc_says_otherwise()
    {
        await _phone.Sync.SyncAsync(Ct);

        Shown.InUse.ShouldBe(new TypesInUseDto(Movies: true, Comics: true));
    }

    [Fact]
    public async Task A_sync_brings_the_types_the_pc_has_in_use_and_tells_the_screens()
    {
        var told = 0;
        Shown.Changed += () => told++;
        await _pc.Types.SetAsync(new(Movies: true, Comics: false), Ct);

        await _phone.Sync.SyncAsync(Ct);

        Shown.InUse.ShouldBe(new TypesInUseDto(Movies: true, Comics: false));
        told.ShouldBe(1);
    }

    [Fact]
    public async Task What_was_saved_for_a_type_that_is_off_still_reaches_the_phone_and_shows_when_it_is_back_on()
    {
        await _pc.Api.AddComicAsync(new() { Series = "Saga", SeriesYear = 2012, Number = "7" }, Ct);
        await _pc.Types.SetAsync(new(Movies: true, Comics: false), Ct);
        await _phone.Sync.SyncAsync(Ct);
        (await _phone.Shelf.ListComicsAsync(new(), Ct)).Facets.IssueCount.ShouldBe(1);

        await _pc.Types.SetAsync(new(Movies: true, Comics: true), Ct);
        await _phone.Sync.SyncAsync(Ct);

        Shown.InUse.ShouldBe(new TypesInUseDto(Movies: true, Comics: true));
    }

    [Fact]
    public async Task The_types_are_remembered_when_the_app_starts_again_away_from_the_pc()
    {
        await _pc.Types.SetAsync(new(Movies: false, Comics: true), Ct);
        await _phone.Sync.SyncAsync(Ct);
        _phone.Away = true;

        using var reopened = new TestPhone(_pc, restartOf: _phone);
        await reopened.StartAsync();

        reopened.Service<ITypesApi>().InUse.ShouldBe(new TypesInUseDto(Movies: false, Comics: true));
        await reopened.Sync.SyncAsync(Ct);
        reopened.Service<ITypesApi>().InUse.ShouldBe(new TypesInUseDto(Movies: false, Comics: true), "a PC that doesn't answer changes nothing");
    }
}
