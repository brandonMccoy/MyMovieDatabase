using System.Globalization;
using MMDb.Application.Comics;
using MMDb.Application.Covers;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Wishlist;

/// <summary>
/// Use cases for the wishlist: browse, want, stop wanting. A film leaves it by itself when it is saved to the
/// collection on the wanted format (see <see cref="MovieCatalog"/>), a comic when a copy of it is saved
/// (see <see cref="ComicCatalog"/>). Every change is mirrored into the vault.
/// </summary>
public sealed class WishlistCatalog(
    IWishlistRepository wishlist,
    IMovieRepository movies,
    IComicRepository comics,
    TimeProvider clock,
    IPosterSource posters,
    IComicMetadata metron,
    ICoverSource comicCovers,
    ICoverStore coverStore,
    VaultMirror vault,
    VaultContentSource content)
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private const int CoverDownloadsAtOnce = 4;

    private readonly CoverKeeper _covers = new(coverStore);

    /// <param name="search">
    /// Words that must all be in a film's title, a director's name or the year; for a comic in its name, series,
    /// publisher or the year its series began. Ignores case and accents.
    /// </param>
    public async Task<WishlistContents> ListAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var all = await wishlist.ListMoviesAsync(cancellationToken);
        var allComics = await wishlist.ListComicsAsync(cancellationToken);
        var terms = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var shown = all
            .Where(m => terms.All(term => Matches(m, term)))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        var shownComics = allComics
            .Where(c => terms.All(term => Matches(c, term)))
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Series, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Number, IssueNumber.Comparer)
            .ToList();

        var owned = await OwnedAsync([.. shown.Select(m => m.TmdbId).OfType<int>()], cancellationToken);
        return new([.. shown.Select(m => new WantedFilm(m, OwnedFormats(owned, m)))], all.Count, shownComics, allComics.Count);
    }

    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    public async Task<WantedFilm?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await wishlist.FindMovieAsync(id, cancellationToken) is not { } item)
        {
            return null;
        }

        return new WantedFilm(item, OwnedFormats(await OwnedAsync(item.TmdbId is { } tmdbId ? [tmdbId] : [], cancellationToken), item));
    }

    /// <returns><see langword="null"/> if it isn't on the wishlist.</returns>
    public Task<WishlistComic?> GetComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        wishlist.FindComicAsync(id, cancellationToken);

    /// <summary>Wants a film already on the wishlist on another format.</summary>
    public async Task<WantMovieResult> ChangeFormatAsync(Guid id, MediaFormat format, CancellationToken cancellationToken = default)
    {
        if (await wishlist.FindMovieAsync(id, cancellationToken) is not { } item)
        {
            return new WantMovieResult.NotFound();
        }

        if (!Enum.IsDefined(format))
        {
            return new WantMovieResult.Invalid([new(WishlistFields.Format, "Choose the format you want.")]);
        }

        var owned = await OwnedAsync(item.TmdbId is { } tmdbId ? [tmdbId] : [], cancellationToken);
        if (item.TmdbId is { } film && owned[film].FirstOrDefault(m => m.Owns(format)) is { } movie)
        {
            return new WantMovieResult.AlreadyOwned(movie);
        }

        if (item.Format != format)
        {
            item.WantOn(format, clock.GetUtcNow());
            try
            {
                await wishlist.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                return new WantMovieResult.Conflict();
            }

            await vault.WishlistChangedAsync(content);
        }

        return new WantMovieResult.Saved(new WantedFilm(item, OwnedFormats(owned, item)));
    }

    /// <summary>
    /// Puts a film on the wishlist, downloading its poster if it has one. A film already on it stays one item:
    /// what is known about it and the wanted format are replaced.
    /// </summary>
    public async Task<WantMovieResult> WantMovieAsync(WantedMovie wanted, CancellationToken cancellationToken = default)
    {
        var errors = WishlistMovie.Validate(wanted);
        if (errors.Count > 0)
        {
            return new WantMovieResult.Invalid(errors);
        }

        var ids = wanted.TmdbId is { } tmdbId ? new[] { tmdbId } : [];
        var owned = await OwnedAsync(ids, cancellationToken);
        if (wanted.TmdbId is { } id && owned[id].FirstOrDefault(m => m.Owns(wanted.Format)) is { } film)
        {
            return new WantMovieResult.AlreadyOwned(film);
        }

        var item = (await wishlist.FindMoviesForAsync(ids, cancellationToken)).FirstOrDefault(m => m.IsFor(wanted.TmdbId, wanted.Title, wanted.Year));
        if (item is null)
        {
            item = WishlistMovie.Create(wanted, clock.GetUtcNow());
            wishlist.Add(item);
        }
        else
        {
            item.Want(wanted, clock.GetUtcNow());
        }

        try
        {
            var poster = item.PosterPath;
            await _covers.SaveAsync(
                item,
                poster is null ? null : CoverKeeper.CoverName(item.Id, poster.TrimStart('/')),
                ct => posters.FetchAsync(poster!, ct),
                () => wishlist.SaveChangesAsync(cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new WantMovieResult.Conflict();
        }

        await vault.WishlistChangedAsync(content);
        return new WantMovieResult.Saved(new WantedFilm(item, OwnedFormats(owned, item)));
    }

    /// <returns><see langword="false"/> if it wasn't on the wishlist.</returns>
    public async Task<bool> RemoveMovieAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await wishlist.FindMovieAsync(id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        wishlist.Remove(item);
        try
        {
            await wishlist.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Removed (or bought) elsewhere in the meantime.
            return false;
        }

        _covers.Forget(item);
        await vault.WishlistChangedAsync(content);
        return true;
    }

    /// <summary>
    /// Puts an issue or a collected edition on the wishlist, downloading its cover if it has one. Something
    /// already wanted stays as it is.
    /// </summary>
    public async Task<WantComicResult> WantComicAsync(WantedComic wanted, CancellationToken cancellationToken = default)
    {
        var errors = WishlistComic.Validate(wanted);
        if (errors.Count > 0)
        {
            return new WantComicResult.Invalid(errors);
        }

        if (wanted.MetronId is { } metronId && await comics.ListByMetronIdsAsync([metronId], cancellationToken) is [var owned, ..])
        {
            return new WantComicResult.AlreadyOwned(owned);
        }

        var details = wanted.AsDetails();
        if ((await wishlist.LoadComicsAsync(cancellationToken)).FirstOrDefault(w => w.IsFor(details)) is { } already)
        {
            return new WantComicResult.Saved(already);
        }

        var item = WishlistComic.Create(wanted, clock.GetUtcNow());
        wishlist.Add(item);
        try
        {
            var url = item.CoverUrl;
            await _covers.SaveAsync(
                item,
                url is null ? null : ComicCatalog.CoverName(item.Id, url),
                ct => comicCovers.FetchAsync(url!, ct),
                () => wishlist.SaveChangesAsync(cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new WantComicResult.Conflict();
        }

        await vault.WishlistChangedAsync(content);
        return new WantComicResult.Saved(item);
    }

    /// <summary>
    /// Puts every issue of a run missing from a series ("#159–167") on the wishlist, each as an item of its own,
    /// with the cover Metron's list of the series' issues shows for it. Issues of the run that are wanted already
    /// stay as they are.
    /// </summary>
    /// <param name="from">The first missing issue.</param>
    /// <param name="through">The last one; <see langword="null"/> for a single issue.</param>
    /// <returns><see cref="WantComicResult.Saved"/> with the first issue of the run.</returns>
    public async Task<WantComicResult> WantMissingAsync(int metronSeriesId, string from, string? through, CancellationToken cancellationToken = default)
    {
        if (await comics.FindSeriesAsync(metronSeriesId, cancellationToken) is not { } series)
        {
            return new WantComicResult.NotFound();
        }

        var numbers = series.IssueNumbers.ToList();
        var first = numbers.FindIndex(n => string.Equals(n, IssueNumber.Normalize(from), StringComparison.OrdinalIgnoreCase));
        var last = through is null ? first : numbers.FindIndex(n => string.Equals(n, IssueNumber.Normalize(through), StringComparison.OrdinalIgnoreCase));
        if (first < 0 || last < first)
        {
            return new WantComicResult.NotFound();
        }

        var wanted = await wishlist.LoadComicsAsync(cancellationToken);
        var run = numbers[first..(last + 1)]
            .Select(number => (Number: number, Item: wanted.FirstOrDefault(w => w.IsIssue(metronSeriesId, series.Name, series.YearBegan, number))))
            .ToList();
        if (run.TrueForAll(issue => issue.Item is not null))
        {
            return new WantComicResult.Saved(run[0].Item!);
        }

        var listed = await ListedAsync(metronSeriesId, cancellationToken);
        var now = clock.GetUtcNow();
        var added = new List<WishlistComic>();
        for (var i = 0; i < run.Count; i++)
        {
            if (run[i].Item is null)
            {
                var issue = listed.GetValueOrDefault(run[i].Number);
                var item = WishlistComic.Create(
                    new WantedComic
                    {
                        Series = series.Name,
                        SeriesYear = series.YearBegan,
                        Publisher = series.Publisher,
                        MetronSeriesId = metronSeriesId,
                        Number = run[i].Number,
                        MetronId = issue?.MetronId,
                        CoverUrl = issue?.CoverUrl,
                        FromMissing = true,
                    },
                    now);
                wishlist.Add(item);
                added.Add(item);
                run[i] = (run[i].Number, item);
            }
        }

        await DownloadAsync(added.Where(item => item.CoverUrl is not null).Select(Cover), cancellationToken);
        try
        {
            await wishlist.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            added.ForEach(_covers.Forget);
            return new WantComicResult.Conflict();
        }

        await vault.WishlistChangedAsync(content);
        return new WantComicResult.Saved(run[0].Item!);
    }

    /// <summary>
    /// Tries again for the covers wishes are missing: a film's poster or a comic's cover that could not be
    /// downloaded when it was wanted, and issues wanted before Metron's entry for them was known. Best effort:
    /// what still can't be had stays as it is.
    /// </summary>
    /// <returns>How many wishes got their cover.</returns>
    public async Task<int> FetchMissingCoversAsync(CancellationToken cancellationToken = default)
    {
        var films = await wishlist.FindMoviesMissingCoverAsync(cancellationToken);
        var comicsWithout = (await wishlist.LoadComicsAsync(cancellationToken)).Where(c => c.CoverImage is null).ToList();
        if (films.Count + comicsWithout.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var unlinked = comicsWithout.Where(c => c is { CoverUrl: null, MetronSeriesId: not null, Number: not null } && !c.Format.IsCollected());
        foreach (var series in unlinked.GroupBy(c => c.MetronSeriesId!.Value).ToList())
        {
            var listed = await ListedAsync(series.Key, cancellationToken);
            foreach (var comic in series)
            {
                if (listed.GetValueOrDefault(comic.Number!) is { } issue)
                {
                    comic.Found(issue.MetronId, issue.CoverUrl, now);
                }
            }
        }

        var downloads = films
            .Select(film => (Item: (ProductBase)film, Name: CoverKeeper.CoverName(film.Id, film.PosterPath!.TrimStart('/')), Fetch: (Func<CancellationToken, Task<byte[]?>>)(ct => posters.FetchAsync(film.PosterPath!, ct))))
            .Concat(comicsWithout.Where(c => c.CoverUrl is not null).Select(Cover))
            .ToList();
        await DownloadAsync(downloads, cancellationToken);
        var got = downloads.Select(d => d.Item).Where(item => item.CoverImage is not null).ToList();
        try
        {
            await wishlist.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Something was bought or dropped meanwhile; the next start tries again for the rest.
            got.ForEach(_covers.Forget);
            return 0;
        }

        if (got.Count > 0)
        {
            await vault.WishlistChangedAsync(content);
        }

        return got.Count;
    }

    private (ProductBase Item, string Name, Func<CancellationToken, Task<byte[]?>> Fetch) Cover(WishlistComic comic) =>
        (comic, ComicCatalog.CoverName(comic.Id, comic.CoverUrl!), ct => comicCovers.FetchAsync(comic.CoverUrl!, ct));

    // Many covers at once (a long run of missing issues): a few at a time, each kept as it arrives. The caller
    // then writes the items once.
    private Task DownloadAsync(IEnumerable<(ProductBase Item, string Name, Func<CancellationToken, Task<byte[]?>> Fetch)> covers, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            covers,
            new ParallelOptions { MaxDegreeOfParallelism = CoverDownloadsAtOnce, CancellationToken = cancellationToken },
            async (cover, ct) => await _covers.SaveAsync(cover.Item, cover.Name, cover.Fetch, () => Task.CompletedTask, ct));

    // Best effort: without Metron's list the issues are still wanted, just without a cover or a link to their entry.
    private async Task<Dictionary<string, IssueListing>> ListedAsync(int metronSeriesId, CancellationToken cancellationToken)
    {
        var listed = new Dictionary<string, IssueListing>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var issue in await metron.ListIssuesAsync(metronSeriesId, cancellationToken))
            {
                if (IssueNumber.Normalize(issue.Number) is { } number)
                {
                    listed.TryAdd(number, issue);
                }
            }
        }
        catch (MetadataException)
        {
        }

        return listed;
    }

    /// <returns><see langword="false"/> if it wasn't on the wishlist.</returns>
    public async Task<bool> RemoveComicAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await wishlist.FindComicAsync(id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        wishlist.Remove(item);
        try
        {
            await wishlist.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }

        _covers.Forget(item);
        await vault.WishlistChangedAsync(content);
        return true;
    }

    private async Task<ILookup<int?, MovieListing>> OwnedAsync(int[] tmdbIds, CancellationToken cancellationToken) =>
        (tmdbIds.Length == 0 ? [] : await movies.ListByTmdbIdsAsync(tmdbIds, cancellationToken)).ToLookup(m => m.TmdbId);

    private static IReadOnlyList<MediaFormat> OwnedFormats(ILookup<int?, MovieListing> owned, WishlistMovie item) =>
        item.TmdbId is { } id ? [.. owned[id].SelectMany(m => m.Formats).Distinct().Order()] : [];

    private static bool Matches(WishlistMovie movie, string term) =>
        Compare.IndexOf(movie.Title, term, Loose) >= 0
        || movie.Directors.Any(d => Compare.IndexOf(d, term, Loose) >= 0)
        || movie.Year?.ToString(CultureInfo.InvariantCulture) == term;

    private static bool Matches(WishlistComic comic, string term) =>
        Compare.IndexOf(comic.DisplayName, term, Loose) >= 0
        || (comic.Series is { } series && Compare.IndexOf(series, term, Loose) >= 0)
        || (comic.Publisher is { } publisher && Compare.IndexOf(publisher, term, Loose) >= 0)
        || comic.SeriesYear?.ToString(CultureInfo.InvariantCulture) == term;
}

/// <param name="Movies">The films matching the search, newest first.</param>
/// <param name="MovieCount">Films on the whole wishlist.</param>
/// <param name="Comics">The comics matching the search, newest first.</param>
/// <param name="ComicCount">Comics on the whole wishlist.</param>
public sealed record WishlistContents(IReadOnlyList<WantedFilm> Movies, int MovieCount, IReadOnlyList<WishlistComic> Comics, int ComicCount);

/// <param name="Owned">The formats the film is owned on already: wanting another one is an upgrade.</param>
public sealed record WantedFilm(WishlistMovie Item, IReadOnlyList<MediaFormat> Owned);

/// <summary>Outcome of putting a film on the wishlist.</summary>
public abstract record WantMovieResult
{
    private WantMovieResult()
    {
    }

    public sealed record Saved(WantedFilm Film) : WantMovieResult;

    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : WantMovieResult;

    /// <summary>The film is in the collection on that format already.</summary>
    public sealed record AlreadyOwned(MovieListing Movie) : WantMovieResult;

    /// <summary>It isn't on the wishlist (any more).</summary>
    public sealed record NotFound : WantMovieResult;

    /// <summary>The item was changed or removed elsewhere at the same moment.</summary>
    public sealed record Conflict : WantMovieResult;
}

/// <summary>Outcome of putting a comic on the wishlist.</summary>
public abstract record WantComicResult
{
    private WantComicResult()
    {
    }

    public sealed record Saved(WishlistComic Comic) : WantComicResult;

    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : WantComicResult;

    /// <summary>A copy of the issue is in the collection already.</summary>
    public sealed record AlreadyOwned(Comic Comic) : WantComicResult;

    /// <summary>The series or its issues aren't known (any more).</summary>
    public sealed record NotFound : WantComicResult;

    /// <summary>The wishlist was changed elsewhere at the same moment.</summary>
    public sealed record Conflict : WantComicResult;
}
