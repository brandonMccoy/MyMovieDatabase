using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Wishlist;
using MMDb.Infrastructure.Persistence;
using MMDb.Local;

namespace MMDb.Client;

/// <summary>
/// <see cref="ICollectionApi"/> for the phone. Everything is read from the phone's own copy and looked up by
/// the phone itself, so it works away from home. What is saved goes into the copy at once and waits in the
/// <see cref="Outbox"/> to be asked of the PC, whose answer then replaces it (see <see cref="ShelfSync"/>).
/// </summary>
public sealed class PhoneCollectionApi(InProcessCollectionApi local, ShelfSync sync, Outbox outbox, CollectionCopy copy) : ICollectionApi
{
    public event Action? Changed
    {
        add => local.Changed += value;
        remove => local.Changed -= value;
    }

    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default) =>
        local.ListMoviesAsync(request, cancellationToken);

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) => local.GetMovieAsync(id, cancellationToken);

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var saved = await local.AddMovieAsync(request, cancellationToken);
                if (saved is { Status: SaveStatus.Saved, Movie: { } film })
                {
                    // A film new to the copy is at its first version; one that got a format added is past it.
                    await outbox.AddAsync(
                        PendingKind.AddMovie, request, film.Id, creates: film.Version == 1, $"{Name(film.Title, film.Year)} on {Formats(request.Formats.Select(f => f.Format))}", cancellationToken);
                }

                return saved;
            },
            cancellationToken);

    /// <summary>Saved films are edited on the PC.</summary>
    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Films are edited on the PC.");

    /// <summary>Only a film added on this phone and not sent yet can be removed here; anything else is removed on the PC.</summary>
    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) => RemoveUnsentAsync("Movies", id, cancellationToken);

    public Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default) => local.LookupMovieAsync(query, cancellationToken);

    public Task<ComicListResponse> ListComicsAsync(ComicListRequest request, CancellationToken cancellationToken = default) =>
        local.ListComicsAsync(request, cancellationToken);

    public Task<ComicDto?> GetComicAsync(Guid id, CancellationToken cancellationToken = default) => local.GetComicAsync(id, cancellationToken);

    public Task<SaveComicResponse> AddComicAsync(SaveComicRequest request, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var saved = await local.AddComicAsync(request, cancellationToken);
                if (saved is { Status: SaveStatus.Saved, Comic: { } comic })
                {
                    await outbox.AddAsync(PendingKind.AddComic, request, comic.Id, creates: true, comic.DisplayName, cancellationToken);
                }

                return saved;
            },
            cancellationToken);

    /// <summary>Saved comics are edited on the PC.</summary>
    public Task<SaveComicResponse> UpdateComicAsync(Guid id, int version, SaveComicRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Comics are edited on the PC.");

    /// <summary>Only a comic added on this phone and not sent yet can be removed here; anything else is removed on the PC.</summary>
    public Task<bool> DeleteComicAsync(Guid id, CancellationToken cancellationToken = default) => RemoveUnsentAsync("Comics", id, cancellationToken);

    public Task<ComicLookupResponse> LookupComicAsync(string query, CancellationToken cancellationToken = default) => local.LookupComicAsync(query, cancellationToken);

    public Task<ScanResponse> LookupScanAsync(ScanRequest request, CancellationToken cancellationToken = default) => local.LookupScanAsync(request, cancellationToken);

    public Task<WishlistResponse> ListWishlistAsync(string? search = null, CancellationToken cancellationToken = default) =>
        local.ListWishlistAsync(search, cancellationToken);

    public Task<WishlistMovieDto?> GetWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) => local.GetWishlistMovieAsync(id, cancellationToken);

    public Task<WishlistComicDto?> GetWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) => local.GetWishlistComicAsync(id, cancellationToken);

    public Task<WantMovieResponse> ChangeWishlistFormatAsync(Guid id, MovieFormatKind format, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var changed = await local.ChangeWishlistFormatAsync(id, format, cancellationToken);
                if (changed is { Status: SaveStatus.Saved, Movie: { } wish })
                {
                    // Not sent yet: the PC is simply asked for the new format in the first place.
                    if (outbox.Creator(id) is { Kind: PendingKind.WantMovie } wanting)
                    {
                        await outbox.RewriteAsync(wanting.Id, wanting.Read<WantMovieRequest>() with { Format = format }, wanting.What, cancellationToken);
                    }
                    else
                    {
                        await outbox.AddAsync(
                            PendingKind.ChangeWishFormat, new ChangeWishFormatRequest(format), id, creates: false, $"{Name(wish.Title, wish.Year)} wanted on {Formats([format])}", cancellationToken);
                    }
                }

                return changed;
            },
            cancellationToken);

    public Task<WantMovieResponse> WantMovieAsync(WantMovieRequest request, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var before = await WishIdsAsync(cancellationToken);
                var wanted = await local.WantMovieAsync(request, cancellationToken);
                if (wanted is { Status: SaveStatus.Saved, Movie: { } wish })
                {
                    var what = $"{Name(wish.Title, wish.Year)} on the wishlist";
                    if (outbox.Creator(wish.Id) is { } wanting)
                    {
                        await outbox.RewriteAsync(wanting.Id, request, what, cancellationToken);
                    }
                    else
                    {
                        await outbox.AddAsync(PendingKind.WantMovie, request, wish.Id, creates: !before.Contains(wish.Id), what, cancellationToken);
                    }
                }

                return wanted;
            },
            cancellationToken);

    public Task<bool> RemoveWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var wish = await local.GetWishlistMovieAsync(id, cancellationToken);
                var removed = await local.RemoveWishlistMovieAsync(id, cancellationToken);
                if (removed)
                {
                    await RemovedAsync(PendingKind.RemoveWishMovie, id, wish is null ? "A film" : Name(wish.Title, wish.Year), cancellationToken);
                }

                return removed;
            },
            cancellationToken);

    public Task<MovieLookupResponse?> LookupWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) => local.LookupWishlistMovieAsync(id, cancellationToken);

    public Task<WantComicResponse> WantComicAsync(WantComicRequest request, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var before = await WishIdsAsync(cancellationToken);
                var wanted = await local.WantComicAsync(request, cancellationToken);

                // Something already wanted stays as it is: nothing to tell the PC.
                if (wanted is { Status: SaveStatus.Saved, Comic: { } wish } && !before.Contains(wish.Id))
                {
                    await outbox.AddAsync(PendingKind.WantComic, request, wish.Id, creates: true, $"{wish.DisplayName} on the wishlist", cancellationToken);
                }

                return wanted;
            },
            cancellationToken);

    public Task<WantComicResponse> WantMissingIssuesAsync(WantMissingRequest request, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var before = await WishIdsAsync(cancellationToken);
                var wanted = await local.WantMissingIssuesAsync(request, cancellationToken);
                if (wanted.Status == SaveStatus.Saved)
                {
                    // Each issue is an item of its own on the wishlist, so each is asked for by itself: any of
                    // them can then be taken off again before it is sent.
                    foreach (var wish in (await local.ListWishlistAsync(null, cancellationToken)).Comics.Where(c => !before.Contains(c.Id) && c.Number is not null).Reverse())
                    {
                        await outbox.AddAsync(
                            PendingKind.WantMissing, new WantMissingRequest(request.MetronSeriesId, wish.Number!), wish.Id, creates: true, $"{wish.DisplayName} on the wishlist", cancellationToken);
                    }
                }

                return wanted;
            },
            cancellationToken);

    public Task<bool> RemoveWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        sync.ChangeAsync(
            async () =>
            {
                var wish = await local.GetWishlistComicAsync(id, cancellationToken);
                var removed = await local.RemoveWishlistComicAsync(id, cancellationToken);
                if (removed)
                {
                    await RemovedAsync(PendingKind.RemoveWishComic, id, wish?.DisplayName ?? "A comic", cancellationToken);
                }

                return removed;
            },
            cancellationToken);

    public Task<ComicLookupResponse?> LookupWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) => local.LookupWishlistComicAsync(id, cancellationToken);

    // A wish the PC never heard of needs no removing there: the asking for it is dropped instead.
    private Task RemovedAsync(PendingKind kind, Guid id, string name, CancellationToken cancellationToken) =>
        outbox.Creator(id) is not null
            ? outbox.ForgetAsync(id, cancellationToken)
            : outbox.AddAsync(kind, new { }, id, creates: false, $"{name} off the wishlist", cancellationToken);

    private Task<bool> RemoveUnsentAsync(string table, Guid id, CancellationToken cancellationToken) =>
        outbox.Creator(id) is null
            ? throw new NotSupportedException("What the PC has is removed on the PC.")
            : sync.ChangeAsync(
                async () =>
                {
                    // As SQLite holds a Guid.
                    var removed = await copy.RemoveAsync(table, [id.ToString().ToUpperInvariant()], cancellationToken) > 0;
                    await outbox.ForgetAsync(id, cancellationToken);
                    local.RaiseChanged();
                    return removed;
                },
                cancellationToken);

    private async Task<HashSet<Guid>> WishIdsAsync(CancellationToken cancellationToken)
    {
        var wishlist = await local.ListWishlistAsync(null, cancellationToken);
        return [.. wishlist.Movies.Select(m => m.Id), .. wishlist.Comics.Select(c => c.Id)];
    }

    private static string Name(string title, int? year) => year is null ? title : $"{title} ({year})";

    private static string Formats(IEnumerable<MovieFormatKind> formats) => string.Join(" and ", formats.Select(format => format switch
    {
        MovieFormatKind.Uhd4K => "4K UHD",
        MovieFormatKind.BluRay => "Blu-ray",
        MovieFormatKind.Dvd => "DVD",
        _ => "Digital",
    }));
}
