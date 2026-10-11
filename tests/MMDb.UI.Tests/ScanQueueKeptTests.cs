using Microsoft.Extensions.DependencyInjection;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Testing;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>The phone's scan queue across closing the app, and with no signal.</summary>
public sealed class ScanQueueKeptTests : PhoneTestContext
{
    private const string HeatUpc = "786936894424";
    private const string OtherUpc = "043396000001";

    private readonly MemoryStore _store = new();

    public ScanQueueKeptTests() => Services.AddSingleton<IScanQueueStore>(_store);

    [Fact]
    public async Task What_is_scanned_is_kept_until_it_is_removed()
    {
        var (film, _) = Queue.Add(HeatUpc);
        Queue.Mode = ScanMode.Comic;
        var (comic, _) = Queue.Add(OtherUpc);
        await Task.WhenAll(film.Lookup, comic.Lookup);

        _store.Kept.ShouldBe([new QueuedScan(HeatUpc), new QueuedScan(OtherUpc, ProductKind.Comic)]);

        Queue.Remove(film.Id);

        _store.Kept.ShouldBe([new QueuedScan(OtherUpc, ProductKind.Comic)]);
    }

    [Fact]
    public async Task What_was_waiting_when_the_app_was_closed_is_back_and_looked_up_again()
    {
        Online.Barcodes.Add(HeatUpc, "Heat [Blu-ray]");
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _store.Kept = [new(HeatUpc), new(OtherUpc, ProductKind.Comic)];
        var told = 0;
        Queue.Changed += () => told++;

        await Queue.RestoreAsync();
        await Task.WhenAll(Queue.Items.Select(i => i.Lookup));

        Queue.Items.Select(i => (i.Code, i.AskedAs, i.State)).ShouldBe([(HeatUpc, null, ScanState.Ready), (OtherUpc, ProductKind.Comic, ScanState.Ready)]);
        Queue.Items[0].Title.ShouldBe("Heat (1995)");
        told.ShouldBeGreaterThan(0);

        await Queue.RestoreAsync();

        Queue.Items.Count.ShouldBe(2, "restoring again adds nothing");
    }

    [Fact]
    public async Task With_no_signal_a_scan_waits_and_is_tried_again_when_the_queue_is_next_on_screen()
    {
        Online.Barcodes.Add(HeatUpc, "Heat [Blu-ray]");
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        await Queue.RestoreAsync();
        Signal = false;

        var (item, _) = Queue.Add(HeatUpc);
        await item.Lookup;

        item.State.ShouldBe(ScanState.Failed);
        item.Response.ShouldBeNull();
        _store.Kept.ShouldBe([new QueuedScan(HeatUpc)]);

        Signal = true;
        await Queue.RestoreAsync();

        item.State.ShouldBe(ScanState.Ready);
        item.Title.ShouldBe("Heat (1995)");
    }

    [Fact]
    public async Task A_barcode_the_phone_has_in_its_copy_is_answered_with_no_signal()
    {
        await SeedAsync(Heat);
        Signal = false;

        var (item, _) = Queue.Add(HeatUpc);
        await item.Lookup;

        item.State.ShouldBe(ScanState.Ready);
        item.IsOwned.ShouldBeTrue();
        item.Response.ShouldNotBeNull().Movie.ShouldNotBeNull().Status.ShouldBe(LookupStatus.BarcodeOwned);
    }

    private sealed class MemoryStore : IScanQueueStore
    {
        public IReadOnlyList<QueuedScan> Kept { get; set; } = [];

        public Task<IReadOnlyList<QueuedScan>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Kept);

        public Task SaveAsync(IReadOnlyList<QueuedScan> scans, CancellationToken cancellationToken = default)
        {
            Kept = scans;
            return Task.CompletedTask;
        }
    }
}
