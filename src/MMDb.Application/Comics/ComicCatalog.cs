using System.Security.Cryptography;
using System.Text;
using MMDb.Application.Covers;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Vault;
using MMDb.Domain.Comics;

namespace MMDb.Application.Comics;

/// <summary>
/// Use cases for the comic collection: browse, add, edit, delete. Saving a copy downloads its cover, fetches
/// its series' issue list from Metron when that is missing or a week old, and mirrors the change into the vault.
/// </summary>
public sealed class ComicCatalog(
    IComicRepository comics,
    TimeProvider clock,
    IComicMetadata metron,
    ICoverSource coverSource,
    ICoverStore coverStore,
    VaultMirror vault,
    VaultContentSource content)
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private readonly CoverKeeper _covers = new(coverStore);

    public Task<Comic?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        comics.FindAsync(id, cancellationToken);

    public async Task<ComicShelf> ListAsync(ComicQuery query, CancellationToken cancellationToken = default) =>
        ComicFilter.Apply(await comics.ListAsync(cancellationToken), await comics.ListSeriesAsync(cancellationToken), query);

    /// <summary>The run a single issue belongs to, with every issue owned and missing; <see langword="null"/> for a collected edition.</summary>
    public async Task<SeriesRun?> RunOfAsync(Comic comic, CancellationToken cancellationToken = default)
    {
        if (comic.Format.IsCollected())
        {
            return null;
        }

        var all = await comics.ListAsync(cancellationToken);
        var run = all.Where(c => !c.Format.IsCollected()
            && string.Equals(c.Series, comic.Series, StringComparison.OrdinalIgnoreCase)
            && c.SeriesYear == comic.SeriesYear).ToList();
        var shelf = ComicFilter.Apply(run, await comics.ListSeriesAsync(cancellationToken), new ComicQuery());
        return shelf.Runs.SingleOrDefault();
    }

    /// <summary>Adds a copy. The same copy twice (see <see cref="Comic.IsSameCopyAs"/>) is turned down.</summary>
    public async Task<SaveComicResult> AddAsync(ComicDetails details, CancellationToken cancellationToken = default)
    {
        var errors = details.Validate();
        if (errors.Count > 0)
        {
            return new SaveComicResult.Invalid(errors);
        }

        if (await FindSameCopyAsync(details, cancellationToken) is { } owned)
        {
            return new SaveComicResult.AlreadyOwned(owned);
        }

        var comic = Comic.Create(details, clock.GetUtcNow());
        comics.Add(comic);
        return await SaveAsync(comic, cancellationToken);
    }

    /// <param name="expectedVersion">The <see cref="Domain.ProductBase.Version"/> the caller edited; a newer one means a conflict.</param>
    public async Task<SaveComicResult> UpdateAsync(Guid id, int expectedVersion, ComicDetails details, CancellationToken cancellationToken = default)
    {
        var comic = await comics.FindAsync(id, cancellationToken);
        if (comic is null)
        {
            return new SaveComicResult.NotFound();
        }

        if (comic.Version != expectedVersion)
        {
            return new SaveComicResult.Conflict();
        }

        var errors = details.Validate();
        if (errors.Count > 0)
        {
            return new SaveComicResult.Invalid(errors);
        }

        comic.Update(details, clock.GetUtcNow());
        return await SaveAsync(comic, cancellationToken);
    }

    /// <returns><see langword="false"/> if there was no such comic.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var comic = await comics.FindAsync(id, cancellationToken);
        if (comic is null)
        {
            return false;
        }

        comics.Remove(comic);
        try
        {
            await comics.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }

        _covers.Forget(comic);
        await vault.RemovedAsync(comic.Id, content);
        return true;
    }

    /// <summary>The cover's file name: the copy's id and a hash of the link, so a new link means a new file.</summary>
    internal static string CoverName(Guid comicId, string url)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return CoverKeeper.CoverName(comicId, hash + (ImageExtensions.Contains(extension) ? extension : ".jpg"));
    }

    private async Task<Comic?> FindSameCopyAsync(ComicDetails details, CancellationToken cancellationToken)
    {
        var d = details.Normalized();
        var candidates = new List<Comic>();
        if (Domain.Comics.ComicBarcode.TryParse(d.Barcode, out var barcode))
        {
            candidates.AddRange(await comics.ListByUpcAsync(barcode.Upc, cancellationToken));
        }

        if (Domain.Isbn.TryParse(d.Isbn, out var isbn))
        {
            candidates.AddRange(await comics.ListByIsbnAsync(isbn, cancellationToken));
        }

        if (d.MetronId is { } metronId)
        {
            candidates.AddRange(await comics.ListByMetronIdsAsync([metronId], cancellationToken));
        }

        var match = candidates.FirstOrDefault(c => c.IsSameCopyAs(d));
        return match is null ? null : await comics.FindAsync(match.Id, cancellationToken);
    }

    private async Task<SaveComicResult> SaveAsync(Comic comic, CancellationToken cancellationToken)
    {
        try
        {
            var url = comic.CoverUrl;
            await _covers.SaveAsync(
                comic,
                url is null ? null : CoverName(comic.Id, url),
                ct => coverSource.FetchAsync(url!, ct),
                () => comics.SaveChangesAsync(cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return new SaveComicResult.Conflict();
        }

        await RefreshSeriesAsync(comic, cancellationToken);
        await vault.ComicSavedAsync(comic, content);
        return new SaveComicResult.Saved(comic);
    }

    // Best effort: without the issue list the run just has no "N of M" or missing issues until a later save.
    private async Task RefreshSeriesAsync(Comic comic, CancellationToken cancellationToken)
    {
        if (comic.MetronSeriesId is not { } seriesId)
        {
            return;
        }

        try
        {
            var now = clock.GetUtcNow();
            var stored = await comics.FindSeriesAsync(seriesId, cancellationToken);
            if (stored is not null && !stored.IsStale(now))
            {
                return;
            }

            if (await metron.GetSeriesAsync(seriesId, cancellationToken) is not { } facts)
            {
                return;
            }

            if (stored is null)
            {
                comics.AddSeries(ComicSeries.Create(seriesId, now, facts));
            }
            else
            {
                stored.Refresh(facts, now);
            }

            await comics.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is MetadataException or OperationCanceledException)
        {
            // The copy is saved; the vault still follows it below.
        }
    }
}
