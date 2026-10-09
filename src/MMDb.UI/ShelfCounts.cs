using MMDb.Contracts;

namespace MMDb.UI;

/// <summary>The collection totals shown in the sidebar. Pages call <see cref="RefreshAsync"/> after saving or deleting.</summary>
public sealed class ShelfCounts(ICollectionApi api)
{
    public event Action? Changed;

    public int? Movies { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Movies = (await api.ListMoviesAsync(new(), cancellationToken)).Facets.FilmCount;
        Changed?.Invoke();
    }
}
