using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Wishlist;
using MMDb.Server.Comics;
using MMDb.Server.Movies;
using MMDb.Server.Wishlist;

namespace MMDb.Server;

/// <summary>
/// <see cref="ICollectionApi"/> for the PC app: calls the use cases directly, no HTTP.
/// Each call gets its own DI scope (and so its own database unit of work), like a web request.
/// </summary>
public sealed class InProcessCollectionApi(IServiceScopeFactory scopes, CoverLinks covers) : ICollectionApi
{
    public event Action? Changed;

    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, MovieListResponse>(async catalog => (await catalog.ListAsync(request.ToDomain(), cancellationToken)).ToContract(covers));

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, MovieDto?>(async catalog => (await catalog.GetAsync(id, cancellationToken))?.ToDto(covers));

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<MovieCatalog, SaveMovieResponse>(async catalog => (await catalog.AddAsync(request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<MovieCatalog, SaveMovieResponse>(async catalog => (await catalog.UpdateAsync(id, version, request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        Saved(With<MovieCatalog, bool>(catalog => catalog.DeleteAsync(id, cancellationToken)), deleted => deleted ? SaveStatus.Saved : SaveStatus.NotFound);

    public Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default) =>
        With<MovieLookup, MovieLookupResponse>(async lookup => (await lookup.LookupAsync(query, cancellationToken)).ToContract(covers));

    public Task<ComicListResponse> ListComicsAsync(ComicListRequest request, CancellationToken cancellationToken = default) =>
        With<ComicCatalog, ComicListResponse>(async catalog => (await catalog.ListAsync(request.ToDomain(), cancellationToken)).ToContract(covers));

    public Task<ComicDto?> GetComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<ComicCatalog, ComicDto?>(async catalog => await catalog.GetAsync(id, cancellationToken) is { } comic
            ? comic.ToDto(covers, await catalog.RunOfAsync(comic, cancellationToken))
            : null);

    public Task<SaveComicResponse> AddComicAsync(SaveComicRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<ComicCatalog, SaveComicResponse>(async catalog => (await catalog.AddAsync(request.ToDetails(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<SaveComicResponse> UpdateComicAsync(Guid id, int version, SaveComicRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<ComicCatalog, SaveComicResponse>(async catalog => (await catalog.UpdateAsync(id, version, request.ToDetails(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<bool> DeleteComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        Saved(With<ComicCatalog, bool>(catalog => catalog.DeleteAsync(id, cancellationToken)), deleted => deleted ? SaveStatus.Saved : SaveStatus.NotFound);

    public Task<ComicLookupResponse> LookupComicAsync(string query, CancellationToken cancellationToken = default) =>
        With<ComicLookup, ComicLookupResponse>(async lookup => (await lookup.LookupAsync(query, cancellationToken)).ToContract(covers));

    public Task<ScanResponse> LookupScanAsync(ScanRequest request, CancellationToken cancellationToken = default) =>
        With<ScanLookup, ScanResponse>(async lookup =>
        {
            var result = await lookup.LookupAsync(request.Code, request.Kind is { } kind ? (Domain.ProductKind)(int)kind : null, cancellationToken);
            return new ScanResponse((ProductKind)(int)result.Kind, result.Movie?.ToContract(covers), result.Comic?.ToContract(covers));
        });

    public Task<WishlistResponse> ListWishlistAsync(string? search = null, CancellationToken cancellationToken = default) =>
        With<WishlistCatalog, WishlistResponse>(async wishlist => (await wishlist.ListAsync(search, cancellationToken)).ToContract(covers));

    public Task<WishlistMovieDto?> GetWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<WishlistCatalog, WishlistMovieDto?>(async wishlist => (await wishlist.GetMovieAsync(id, cancellationToken))?.ToDto(covers));

    public Task<WishlistComicDto?> GetWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<WishlistCatalog, WishlistComicDto?>(async wishlist => (await wishlist.GetComicAsync(id, cancellationToken))?.ToDto(covers));

    public Task<WantMovieResponse> ChangeWishlistFormatAsync(Guid id, MovieFormatKind format, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, WantMovieResponse>(async wishlist => (await wishlist.ChangeFormatAsync(id, format.ToDomain(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<WantMovieResponse> WantMovieAsync(WantMovieRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, WantMovieResponse>(async wishlist => (await wishlist.WantMovieAsync(request.ToDomain(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<bool> RemoveWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, bool>(wishlist => wishlist.RemoveMovieAsync(id, cancellationToken)), removed => removed ? SaveStatus.Saved : SaveStatus.NotFound);

    public Task<MovieLookupResponse?> LookupWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<MovieLookup, MovieLookupResponse?>(async lookup => (await lookup.LookupWishAsync(id, cancellationToken))?.ToContract(covers));

    public Task<WantComicResponse> WantComicAsync(WantComicRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, WantComicResponse>(async wishlist => (await wishlist.WantComicAsync(request.ToDomain(), cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<WantComicResponse> WantMissingIssuesAsync(WantMissingRequest request, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, WantComicResponse>(async wishlist =>
            (await wishlist.WantMissingAsync(request.MetronSeriesId, request.From, request.Through, cancellationToken)).ToContract(covers)), r => r.Status);

    public Task<bool> RemoveWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, bool>(wishlist => wishlist.RemoveComicAsync(id, cancellationToken)), removed => removed ? SaveStatus.Saved : SaveStatus.NotFound);

    public Task<ComicLookupResponse?> LookupWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<ComicLookup, ComicLookupResponse?>(async lookup => (await lookup.LookupWishAsync(id, cancellationToken))?.ToContract(covers));

    /// <summary>
    /// Tries again for covers the wishlist is missing (see <see cref="WishlistCatalog.FetchMissingCoversAsync"/>);
    /// the PC does this once when it starts. Open screens are told if any arrived.
    /// </summary>
    /// <returns>How many wishes got their cover.</returns>
    public Task<int> FetchMissingWishlistCoversAsync(CancellationToken cancellationToken = default) =>
        Saved(With<WishlistCatalog, int>(wishlist => wishlist.FetchMissingCoversAsync(cancellationToken)), got => got > 0 ? SaveStatus.Saved : SaveStatus.NotFound);

    /// <summary>Tells open screens of a change made past this API (films added by the folder import).</summary>
    internal void RaiseChanged() => Changed?.Invoke();

    private async Task<TResult> Saved<TResult>(Task<TResult> saving, Func<TResult, SaveStatus> status)
    {
        var result = await saving;
        if (status(result) == SaveStatus.Saved)
        {
            Changed?.Invoke();
        }

        return result;
    }

    private async Task<TResult> With<TService, TResult>(Func<TService, Task<TResult>> use)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
