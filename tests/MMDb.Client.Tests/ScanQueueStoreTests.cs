using MMDb.Contracts;
using MMDb.Contracts.Scans;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>Where the phone keeps its scan queue between runs.</summary>
public sealed class ScanQueueStoreTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _phone = new TestPhone(_pc);
        await _phone.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
    }

    [Fact]
    public async Task The_queue_is_still_there_after_the_app_was_closed()
    {
        var store = _phone.Service<IScanQueueStore>();
        (await store.LoadAsync(Ct)).ShouldBeEmpty();

        await store.SaveAsync([new("786936894424"), new("76194134274900711", ProductKind.Comic)], Ct);

        using var reopened = new TestPhone(_pc, restartOf: _phone);
        await reopened.StartAsync();
        (await reopened.Service<IScanQueueStore>().LoadAsync(Ct)).ShouldBe([new QueuedScan("786936894424"), new QueuedScan("76194134274900711", ProductKind.Comic)]);
    }

    [Fact]
    public async Task An_emptied_queue_leaves_nothing_behind_and_the_last_save_wins()
    {
        var store = _phone.Service<IScanQueueStore>();

        await Task.WhenAll(
            store.SaveAsync([new("1")], Ct),
            store.SaveAsync([new("1"), new("2")], Ct),
            store.SaveAsync([], Ct));

        (await store.LoadAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Syncing_with_the_pc_leaves_the_queue_and_what_waits_to_be_sent_alone()
    {
        await _phone.Link.PairAsync(_phone.QrText(), Ct);
        var store = _phone.Service<IScanQueueStore>();
        await store.SaveAsync([new("786936894424")], Ct);
        await _pc.Api.AddMovieAsync(new() { Title = "Heat", Formats = [new(Contracts.Movies.MovieFormatKind.Dvd)] }, Ct);

        await _phone.Sync.SyncAsync(Ct);

        (await store.LoadAsync(Ct)).ShouldHaveSingleItem().Code.ShouldBe("786936894424");
        (await _phone.Shelf.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
    }
}
