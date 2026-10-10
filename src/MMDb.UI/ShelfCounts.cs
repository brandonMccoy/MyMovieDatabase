using MMDb.Contracts;

namespace MMDb.UI;

/// <summary>The collection totals shown in the sidebar. Pages call <see cref="RefreshAsync"/> after saving or deleting.</summary>
public sealed class ShelfCounts(ICollectionApi api)
{
    public event Action? Changed;

    public int? Movies { get; private set; }

    /// <summary>Comic copies: single issues, trades, hardcovers and omnibuses.</summary>
    public int? Comics { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Movies = (await api.ListMoviesAsync(new(), cancellationToken)).Facets.FilmCount;
        var comics = (await api.ListComicsAsync(new(), cancellationToken)).Facets;
        Comics = comics.IssueCount + comics.CollectedCount;
        Changed?.Invoke();
    }
}
