using MMDb.Contracts;

namespace MMDb.UI;

/// <summary>
/// The collection totals shown in the sidebar. They follow the collection: once read, they are read again
/// whenever something is saved or deleted, on this PC or from a phone.
/// </summary>
public sealed class ShelfCounts : IDisposable
{
    private readonly ICollectionApi _api;
    private bool _wanted;

    public ShelfCounts(ICollectionApi api)
    {
        _api = api;
        api.Changed += OnCollectionChanged;
    }

    public event Action? Changed;

    public int? Movies { get; private set; }

    /// <summary>Comic copies: single issues, trades, hardcovers and omnibuses.</summary>
    public int? Comics { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _wanted = true;
        Movies = (await _api.ListMoviesAsync(new(), cancellationToken)).Facets.FilmCount;
        var comics = (await _api.ListComicsAsync(new(), cancellationToken)).Facets;
        Comics = comics.IssueCount + comics.CollectedCount;
        Changed?.Invoke();
    }

    public void Dispose() => _api.Changed -= OnCollectionChanged;

    // Only once a screen has asked for the totals: the phone never shows them.
    private void OnCollectionChanged()
    {
        if (_wanted)
        {
            _ = RefreshAsync();
        }
    }
}
