using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Metadata;

/// <summary>
/// Finds a film online from what was typed or scanned: a title (optionally with a year) or a UPC.
/// Nothing is saved; the user reviews a match and then adds it through <see cref="MovieCatalog"/>.
/// </summary>
public sealed class MovieLookup(IMovieMetadata metadata, IBarcodeLookup barcodes, IMovieRepository movies, IWishlistRepository wishlist)
{
    /// <summary>At most this many matches are fetched in full and returned.</summary>
    public const int MaxMatches = 8;

    public async Task<MovieLookupResult> LookupAsync(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Upc.TryParse(query, out var upc))
            {
                return await ByBarcodeAsync(upc, cancellationToken);
            }

            var text = SearchText.Parse(query);
            return text.Text.Length == 0
                ? new() { Outcome = LookupOutcome.NoMatches }
                : Found(await SearchAsync(text.Title, text.Year, text.Text, cancellationToken));
        }
        catch (MetadataException ex)
        {
            return new() { Outcome = LookupOutcome.Failed, Problem = ex.Problem, FailedService = ex.Service, Upc = Upc.TryParse(query, out var upc) ? upc : null };
        }
    }

    /// <summary>
    /// Looks up a film on the wishlist that has been bought ("Got it"): its TMDB entry, or for a wish entered
    /// without one, a search for its title.
    /// </summary>
    /// <returns><see langword="null"/> if it isn't on the wishlist (any more).</returns>
    public async Task<MovieLookupResult?> LookupWishAsync(Guid wishId, CancellationToken cancellationToken = default)
    {
        if (await wishlist.FindMovieAsync(wishId, cancellationToken) is not { } wish)
        {
            return null;
        }

        try
        {
            var matches = wish.TmdbId is { } tmdbId
                ? await MatchesAsync([tmdbId], cancellationToken)
                : await SearchAsync(wish.Title, wish.Year, wish.Title, cancellationToken);
            return Found(matches) with { Wish = wish };
        }
        catch (MetadataException ex)
        {
            return new() { Outcome = LookupOutcome.Failed, Problem = ex.Problem, FailedService = ex.Service, Wish = wish };
        }
    }

    private async Task<MovieLookupResult> ByBarcodeAsync(Upc upc, CancellationToken cancellationToken)
    {
        // Already on the shelf: no need to ask anyone (and UPCitemdb's free tier allows 100 lookups a day).
        if (await movies.FindByUpcAsync(upc, cancellationToken) is { } owned)
        {
            return new() { Outcome = LookupOutcome.BarcodeOwned, Upc = upc, OwnedByBarcode = owned };
        }

        var names = await barcodes.FindProductNamesAsync(upc, cancellationToken);
        if (names.Count == 0)
        {
            return new() { Outcome = LookupOutcome.UnknownBarcode, Upc = upc };
        }

        var product = RetailTitle.Parse(names[0]);
        var matches = await SearchAsync(product.Title, product.Year, product.Title, cancellationToken);
        return Found(matches) with
        {
            Upc = upc,
            ProductName = names[0],
            ProductTitle = product.Title.Length > 0 ? product.Title : null,
            Format = product.Format,
        };
    }

    // A trailing year may be part of the title ("Blade Runner 2049"), so no hits with it means try without.
    private async Task<IReadOnlyList<MovieMatch>> SearchAsync(string title, int? year, string fallback, CancellationToken cancellationToken)
    {
        var ids = await metadata.SearchAsync(title, year, cancellationToken);
        if (ids.Count == 0 && year is not null)
        {
            ids = await metadata.SearchAsync(fallback, null, cancellationToken);
        }

        return await MatchesAsync([.. ids.Distinct().Take(MaxMatches)], cancellationToken);
    }

    // Each film in full, with what the collection and the wishlist have of it.
    private async Task<IReadOnlyList<MovieMatch>> MatchesAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken)
    {
        var details = await Task.WhenAll(tmdbIds.Select(id => metadata.GetAsync(id, cancellationToken)));
        var owned = (await movies.ListByTmdbIdsAsync(tmdbIds, cancellationToken)).ToLookup(m => m.TmdbId);
        var wanted = await wishlist.FindMoviesForAsync(tmdbIds, cancellationToken);

        return [.. details.OfType<MovieDetails>().Select(d => new MovieMatch(d, owned[d.TmdbId].FirstOrDefault(), wanted.FirstOrDefault(w => w.IsFor(d))))];
    }

    private static MovieLookupResult Found(IReadOnlyList<MovieMatch> matches) =>
        new() { Outcome = matches.Count > 0 ? LookupOutcome.Found : LookupOutcome.NoMatches, Matches = matches };
}

public enum LookupOutcome
{
    Found,
    NoMatches,

    /// <summary>The barcode database doesn't know the UPC.</summary>
    UnknownBarcode,

    /// <summary>A film in the collection already has this UPC.</summary>
    BarcodeOwned,

    /// <summary>An online database could not answer; see <see cref="MovieLookupResult.Problem"/>.</summary>
    Failed,
}

public sealed record MovieLookupResult
{
    public required LookupOutcome Outcome { get; init; }

    /// <summary>Best match first.</summary>
    public IReadOnlyList<MovieMatch> Matches { get; init; } = [];

    /// <summary>The barcode searched for, if a UPC was entered.</summary>
    public Upc? Upc { get; init; }

    /// <summary>The barcode database's name for the product, e.g. "Heat [Blu-ray]".</summary>
    public string? ProductName { get; init; }

    /// <summary>The film's title as read from the product name, e.g. "Heat": what to search on with.</summary>
    public string? ProductTitle { get; init; }

    /// <summary>The format the product name gives, if any.</summary>
    public MediaFormat? Format { get; init; }

    public Movie? OwnedByBarcode { get; init; }

    /// <summary>The wishlist item the lookup was for (see <see cref="MovieLookup.LookupWishAsync"/>).</summary>
    public WishlistMovie? Wish { get; init; }

    public MetadataProblem? Problem { get; init; }

    public string? FailedService { get; init; }
}

/// <param name="Owned">The film in the collection with the same TMDB id, if there is one.</param>
/// <param name="Wanted">The wishlist item for this film, if it is on the wishlist.</param>
public sealed record MovieMatch(MovieDetails Details, MovieListing? Owned, WishlistMovie? Wanted = null);
