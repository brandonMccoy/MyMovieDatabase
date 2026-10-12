using MMDb.Contracts;

namespace MMDb.UI;

/// <summary>
/// The collection totals shown in the sidebar. They follow the collection: once read, they are read again
/// whenever something is saved or deleted, on this PC or from a phone. A type that isn't in use isn't counted.
/// </summary>
public sealed class ShelfCounts : IDisposable
{
    private readonly ICollectionApi _api;
    private readonly ITypesApi _types;
    private bool _wanted;

    public ShelfCounts(ICollectionApi api, ITypesApi types)
    {
        _api = api;
        _types = types;
        api.Changed += OnCollectionChanged;
        types.Changed += OnCollectionChanged;
    }

    public event Action? Changed;

    public int? Movies { get; private set; }

    /// <summary>Comic copies: single issues, trades, hardcovers and omnibuses.</summary>
    public int? Comics { get; private set; }

    /// <summary>Films and comic items on the wishlist, of the types in use.</summary>
    public int? Wishlist { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _wanted = true;
        var types = _types.InUse;
        Movies = types.Movies ? (await _api.ListMoviesAsync(new(), cancellationToken)).Facets.FilmCount : null;
        if (types.Comics)
        {
            var comics = (await _api.ListComicsAsync(new(), cancellationToken)).Facets;
            Comics = comics.IssueCount + comics.CollectedCount;
        }
        else
        {
            Comics = null;
        }

        var wishlist = await _api.ListWishlistAsync(cancellationToken: cancellationToken);
        Wishlist = (types.Movies ? wishlist.MovieCount : 0) + (types.Comics ? wishlist.ComicCount : 0);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _api.Changed -= OnCollectionChanged;
        _types.Changed -= OnCollectionChanged;
    }

    // Only once a screen has asked for the totals: the phone never shows them.
    private void OnCollectionChanged()
    {
        if (_wanted)
        {
            _ = RefreshAsync();
        }
    }
}
