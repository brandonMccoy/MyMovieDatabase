using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Wishlist;

namespace MMDb.UI.Scanning;

/// <summary>What the scanner takes the next barcode for.</summary>
public enum ScanMode
{
    /// <summary>The code says: an ISBN or a UPC with an add-on is a comic, anything else a film.</summary>
    Auto,
    Movie,
    Comic,
}

public enum ScanState
{
    LookingUp,

    /// <summary>Looked up; <see cref="ScanItem.Response"/> has what was found (which may be nothing).</summary>
    Ready,

    /// <summary>The online databases could not be asked (no signal): it waits to be tried again.</summary>
    Failed,
}

/// <summary>A scanned barcode waiting to be reviewed.</summary>
public sealed class ScanItem
{
    internal ScanItem(string code, ProductKind? askedAs)
    {
        Code = code;
        AskedAs = askedAs;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Code { get; }

    /// <summary>What the user said it is; <see langword="null"/> left it to the code.</summary>
    public ProductKind? AskedAs { get; internal set; }

    public ScanState State { get; internal set; }

    public ScanResponse? Response { get; internal set; }

    /// <summary>The lookup in progress, for callers that want to wait for it.</summary>
    public Task Lookup { get; internal set; } = Task.CompletedTask;

    /// <summary>What it was looked up as, once it was.</summary>
    public ProductKind? Kind => Response?.Kind;

    /// <summary>The best match's name, "Heat (1995)" or "Saga #7"; <see langword="null"/> if nothing was found.</summary>
    public string? Title => Response switch
    {
        { Movie.Candidates: [var film, ..] } => film.Year is { } year ? $"{film.Title} ({year})" : film.Title,
        { Movie.OwnedByBarcode: { } film } => film.Title,
        { Comic.Candidates: [var comic, ..] } => comic.DisplayName,
        { Comic.OwnedByCode: { } comic } => comic.DisplayName,
        _ => null,
    };

    /// <summary>Set if the best match is a film on the wishlist.</summary>
    public MovieWishDto? Wish => Response is { Movie.Candidates: [var film, ..] } ? film.Wish : null;

    /// <summary>Set if the best match is a comic on the wishlist.</summary>
    public ComicWishDto? ComicWish => Response is { Comic.Candidates: [var comic, ..] } ? comic.Wish : null;

    public bool IsWanted => Wish is not null || ComicWish is not null;

    /// <summary>The collection already has this very barcode.</summary>
    public bool IsOwned =>
        Response?.Movie?.Status == LookupStatus.BarcodeOwned || Response?.Comic?.Status == LookupStatus.BarcodeOwned;
}

/// <summary>
/// The phone's scan queue: every barcode scanned is looked up in the background and waits here to be reviewed.
/// With a <see cref="IScanQueueStore"/> (the phone has one) it is still there after the app was closed.
/// </summary>
public sealed class ScanInbox(ICollectionApi api, ITypesApi types, IScanQueueStore? store = null)
{
    private readonly List<ScanItem> _items = [];
    private Task? _restoring;

    public event Action? Changed;

    public ScanMode Mode { get; set; }

    /// <summary>
    /// What the next barcode is looked up as: the one type in use, else what <see cref="Mode"/> says.
    /// <see langword="null"/> leaves it to the code.
    /// </summary>
    public ProductKind? AskedAs => types.InUse.Only ?? Mode switch
    {
        ScanMode.Movie => ProductKind.Movie,
        ScanMode.Comic => ProductKind.Comic,
        _ => null,
    };

    /// <summary>Oldest scan first.</summary>
    public IReadOnlyList<ScanItem> Items => _items;

    public ScanItem? Find(Guid? id) => id is null ? null : _items.FirstOrDefault(i => i.Id == id);

    /// <summary>The next scan there is something to review for.</summary>
    public ScanItem? NextReady => _items.FirstOrDefault(i => i.State == ScanState.Ready);

    /// <summary>Queues a code and starts looking it up. A code already waiting isn't queued twice.</summary>
    /// <returns>The scan, and whether it is new.</returns>
    public (ScanItem Item, bool IsNew) Add(string code)
    {
        var digits = (code ?? "").Trim();
        if (_items.FirstOrDefault(i => i.Code == digits) is { } waiting)
        {
            return (waiting, false);
        }

        var item = new ScanItem(digits, AskedAs);
        _items.Add(item);
        item.Lookup = LookUpAsync(item);
        Keep();
        return (item, true);
    }

    /// <summary>
    /// Brings back what was waiting when the app was last closed and looks it up again (what was found before
    /// is remembered, so that costs nothing), and tries again for what couldn't be looked up. Called whenever
    /// the queue comes on screen.
    /// </summary>
    public Task RestoreAsync()
    {
        _restoring ??= LoadAsync();
        return _restoring.IsCompleted ? RetryFailedAsync() : _restoring;
    }

    private async Task LoadAsync()
    {
        if (store is null)
        {
            return;
        }

        foreach (var kept in await store.LoadAsync())
        {
            if (!_items.Exists(i => i.Code == kept.Code))
            {
                var item = new ScanItem(kept.Code, kept.Kind);
                _items.Add(item);
                item.Lookup = LookUpAsync(item);
            }
        }

        Changed?.Invoke();
    }

    private Task RetryFailedAsync() =>
        Task.WhenAll(_items.Where(i => i.State == ScanState.Failed).ToList().Select(i => RetryAsync(i)));

    private void Keep() => _ = store?.SaveAsync([.. _items.Select(i => new QueuedScan(i.Code, i.AskedAs))]);

    /// <summary>Looks the code up again, as a film or a comic if given: a comic whose add-on wasn't read looks like a film.</summary>
    public Task RetryAsync(ScanItem item, ProductKind? asKind = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.AskedAs = asKind ?? item.AskedAs;
        item.Lookup = LookUpAsync(item);
        Keep();
        return item.Lookup;
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
        {
            Keep();
            Changed?.Invoke();
        }
    }

    private async Task LookUpAsync(ScanItem item)
    {
        item.State = ScanState.LookingUp;
        item.Response = null;
        Changed?.Invoke();
        try
        {
            var response = await api.LookupScanAsync(new ScanRequest(item.Code, item.AskedAs));

            // No signal: nothing to review yet. What the phone knows by itself (a barcode it owns) needs none.
            if (response.Movie?.Status == LookupStatus.Unavailable || response.Comic?.Status == LookupStatus.Unavailable)
            {
                item.State = ScanState.Failed;
            }
            else
            {
                item.Response = response;
                item.State = ScanState.Ready;
            }
        }
        catch (PcUnavailableException)
        {
            item.State = ScanState.Failed;
        }

        Changed?.Invoke();
    }
}
