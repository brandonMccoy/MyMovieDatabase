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

    /// <summary>The PC answered; <see cref="ScanItem.Response"/> has what it found (which may be nothing).</summary>
    Ready,

    /// <summary>The PC could not be asked.</summary>
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

    /// <summary>What it was looked up as, once the PC answered.</summary>
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
/// The phone's scan queue: every barcode scanned is looked up by the PC in the background and waits here to
/// be reviewed. It lives as long as the app is open.
/// </summary>
public sealed class ScanInbox(ICollectionApi api)
{
    private readonly List<ScanItem> _items = [];

    public event Action? Changed;

    public ScanMode Mode { get; set; }

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

        var item = new ScanItem(digits, Mode switch
        {
            ScanMode.Movie => ProductKind.Movie,
            ScanMode.Comic => ProductKind.Comic,
            _ => null,
        });
        _items.Add(item);
        item.Lookup = LookUpAsync(item);
        return (item, true);
    }

    /// <summary>Looks the code up again, as a film or a comic if given: a comic whose add-on wasn't read looks like a film.</summary>
    public Task RetryAsync(ScanItem item, ProductKind? asKind = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.AskedAs = asKind ?? item.AskedAs;
        item.Lookup = LookUpAsync(item);
        return item.Lookup;
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
        {
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
            item.Response = await api.LookupScanAsync(new ScanRequest(item.Code, item.AskedAs));
            item.State = ScanState.Ready;
        }
        catch (PcUnavailableException)
        {
            item.State = ScanState.Failed;
        }

        Changed?.Invoke();
    }
}
