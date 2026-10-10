using System.Text.RegularExpressions;
using MMDb.Application.Comics;
using MMDb.Application.Wishlist;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Wishlist;

namespace MMDb.Application.Metadata;

/// <summary>
/// Finds a comic online from what was typed or scanned: an ISBN (a collected edition, from Open Library), a
/// cover barcode, a UPC followed by the issue number, or series words and a number (issues, from Metron).
/// Nothing is saved; the user reviews a match and then adds it through <see cref="ComicCatalog"/>.
/// </summary>
public sealed partial class ComicLookup(IComicMetadata metron, IBookMetadata books, IComicRepository comics, IWishlistRepository wishlist)
{
    /// <summary>At most this many issues are fetched in full (Metron allows 20 requests a minute).</summary>
    public const int MaxIssues = 6;

    /// <summary>At most this many matches are returned, counting each variant cover as one.</summary>
    public const int MaxMatches = 12;

    public const string MetronService = "Metron";
    public const string OpenLibraryService = "Open Library";

    public async Task<ComicLookupResult> LookupAsync(string query, CancellationToken cancellationToken = default)
    {
        var text = (query ?? "").Trim();
        try
        {
            if (Isbn.TryParse(text, out var isbn) && IsbnShape().IsMatch(text))
            {
                return await ByIsbnAsync(isbn, cancellationToken);
            }

            if (UpcAndNumber().Match(text) is { Success: true } typed && Upc.TryParse(typed.Groups["upc"].Value, out var upc))
            {
                var number = IssueNumber.Normalize(typed.Groups["number"].Value);
                var found = await SearchAsync(new IssueSearch { UpcPrefix = upc.Value, Number = number }, cancellationToken);
                return Found(found) with { Barcode = upc.Value };
            }

            if (ComicBarcode.TryParse(text, out var barcode))
            {
                return await ByBarcodeAsync(barcode, cancellationToken);
            }

            var search = ComicSearchText.Parse(text);
            return search.Series.Length == 0
                ? new() { Outcome = LookupOutcome.NoMatches }
                : Found(await SearchAsync(new IssueSearch { Series = search.Series, SeriesYear = search.SeriesYear, Number = search.Number }, cancellationToken));
        }
        catch (MetadataException ex)
        {
            return new() { Outcome = LookupOutcome.Failed, Problem = ex.Problem, FailedService = ex.Service };
        }
    }

    /// <summary>
    /// Looks up a comic on the wishlist that has been bought ("Got it"): its ISBN, its Metron issue, or a search
    /// for its series and number. A book wanted without an ISBN finds nothing: it is searched for or entered by hand.
    /// </summary>
    /// <returns><see langword="null"/> if it isn't on the wishlist (any more).</returns>
    public async Task<ComicLookupResult?> LookupWishAsync(Guid wishId, CancellationToken cancellationToken = default)
    {
        if (await wishlist.FindComicAsync(wishId, cancellationToken) is not { } wish)
        {
            return null;
        }

        try
        {
            if (Isbn.TryParse(wish.Isbn, out var isbn))
            {
                return await ByIsbnAsync(isbn, cancellationToken) with { Wish = wish };
            }

            if (wish.Format.IsCollected() || wish.Number is null)
            {
                return new() { Outcome = LookupOutcome.NoMatches, Wish = wish };
            }

            var matches = wish.MetronId is { } metronId
                ? await MatchesAsync([metronId], scanned: null, cancellationToken)
                : await SearchAsync(new IssueSearch { Series = wish.Series, SeriesYear = wish.SeriesYear, Number = wish.Number }, cancellationToken);
            return Found(matches) with { Wish = wish };
        }
        catch (MetadataException ex)
        {
            return new() { Outcome = LookupOutcome.Failed, Problem = ex.Problem, FailedService = ex.Service, Wish = wish };
        }
    }

    private async Task<ComicLookupResult> ByIsbnAsync(Isbn isbn, CancellationToken cancellationToken)
    {
        if (await comics.ListByIsbnAsync(isbn, cancellationToken) is [var owned, ..])
        {
            return new() { Outcome = LookupOutcome.BarcodeOwned, Barcode = isbn.Value, OwnedByCode = owned };
        }

        if (await books.FindByIsbnAsync(isbn, cancellationToken) is not { } book)
        {
            return new() { Outcome = LookupOutcome.UnknownBarcode, Barcode = isbn.Value };
        }

        var found = book with { Isbn = isbn.Value };
        var wanted = (await wishlist.ListComicsAsync(cancellationToken)).FirstOrDefault(w => w.IsFor(found));
        return Found([new ComicMatch(found, [], wanted)]) with { Barcode = isbn.Value };
    }

    private async Task<ComicLookupResult> ByBarcodeAsync(ComicBarcode barcode, CancellationToken cancellationToken)
    {
        // Already on the shelf (same add-on: same issue, cover and printing): no need to ask Metron.
        var sameUpc = await comics.ListByUpcAsync(barcode.Upc, cancellationToken);
        if (barcode.Supplement is not null && sameUpc.FirstOrDefault(c => c.Barcode?.Supplement == barcode.Supplement) is { } owned)
        {
            return new() { Outcome = LookupOutcome.BarcodeOwned, Barcode = barcode.Value, OwnedByCode = owned };
        }

        var matches = await SearchAsync(new IssueSearch { Barcode = barcode.Value }, cancellationToken, barcode);
        if (matches.Count == 0)
        {
            // Variants and older issues often aren't listed under their full barcode, but the series' UPC and the
            // issue number from the add-on find them.
            matches = await SearchAsync(new IssueSearch { UpcPrefix = barcode.Upc.Value, Number = barcode.AddOn?.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken, barcode);
        }

        return (matches.Count == 0 ? new ComicLookupResult { Outcome = LookupOutcome.UnknownBarcode } : Found(matches)) with { Barcode = barcode.Value };
    }

    /// <param name="scanned">The barcode on the copy in hand: it picks out the variant, and it goes on the copy.</param>
    private async Task<IReadOnlyList<ComicMatch>> SearchAsync(IssueSearch search, CancellationToken cancellationToken, ComicBarcode? scanned = null)
    {
        var ids = (await metron.SearchAsync(search, cancellationToken)).Distinct().Take(MaxIssues).ToList();
        return await MatchesAsync(ids, scanned, cancellationToken);
    }

    // Each issue in full, with what the collection and the wishlist have of it.
    private async Task<IReadOnlyList<ComicMatch>> MatchesAsync(List<int> ids, ComicBarcode? scanned, CancellationToken cancellationToken)
    {
        var issues = (await Task.WhenAll(ids.Select(id => metron.GetIssueAsync(id, cancellationToken)))).OfType<IssueFacts>().ToList();
        var owned = (await comics.ListByMetronIdsAsync(ids, cancellationToken)).ToLookup(c => c.MetronId);
        var wishes = issues.Count == 0 ? [] : await wishlist.ListComicsAsync(cancellationToken);

        var matches = new List<ComicMatch>();
        foreach (var issue in issues)
        {
            var copies = owned[issue.Details.MetronId].ToList();
            var wanted = wishes.FirstOrDefault(w => w.IsFor(issue.Details));
            foreach (var details in Covers(issue, scanned))
            {
                matches.Add(new ComicMatch(details, copies, wanted));
            }
        }

        return [.. matches.Take(MaxMatches)];
    }

    // The regular cover and each variant; for a scanned copy, only the cover it is (the regular one if it can't tell).
    private static IEnumerable<ComicDetails> Covers(IssueFacts issue, ComicBarcode? scanned)
    {
        var regular = issue.Details;
        var variants = issue.Variants.Select(v => regular with
        {
            Variant = v.Name,
            Barcode = v.Barcode,
            CoverUrl = v.CoverUrl ?? regular.CoverUrl,
        });

        if (scanned is null)
        {
            return [regular, .. variants];
        }

        var printing = scanned.AddOn?.Printing is > 1 and var p ? p : regular.Printing;
        var variant = variants.FirstOrDefault(v => ComicBarcode.TryParse(v.Barcode, out var b) && b == scanned) ?? regular;
        return [variant with { Barcode = scanned.Value, Printing = printing }];
    }

    private static ComicLookupResult Found(IReadOnlyList<ComicMatch> matches) =>
        new() { Outcome = matches.Count > 0 ? LookupOutcome.Found : LookupOutcome.NoMatches, Matches = matches };

    // Only codes that look like an ISBN on their own: 10 characters, or 13 digits starting 978/979.
    [GeneratedRegex(@"^(?:97[89][\d -]{10,14}|[\d -]{9,12}[\dXx])$")]
    private static partial Regex IsbnShape();

    // "759606200689 7", "759606200689 #7": the UPC under the bars, then the issue number typed by hand.
    [GeneratedRegex(@"^(?<upc>\d{12})\s+#?(?<number>\S{1,4})$")]
    private static partial Regex UpcAndNumber();
}

public sealed record ComicLookupResult
{
    public required LookupOutcome Outcome { get; init; }

    /// <summary>Best match first. Each variant cover of an issue is a match of its own.</summary>
    public IReadOnlyList<ComicMatch> Matches { get; init; } = [];

    /// <summary>The barcode or ISBN searched for, if one was entered.</summary>
    public string? Barcode { get; init; }

    /// <summary>The copy in the collection with this very barcode or ISBN.</summary>
    public Comic? OwnedByCode { get; init; }

    /// <summary>The wishlist item the lookup was for (see <see cref="ComicLookup.LookupWishAsync"/>).</summary>
    public WishlistComic? Wish { get; init; }

    public MetadataProblem? Problem { get; init; }

    public string? FailedService { get; init; }
}

/// <param name="Owned">Copies in the collection of the same Metron issue (any variant).</param>
/// <param name="Wanted">The wishlist item this issue or book is (part of), if it is wanted.</param>
public sealed record ComicMatch(ComicDetails Details, IReadOnlyList<Comic> Owned, WishlistComic? Wanted = null)
{
    /// <summary>This very copy (same cover and printing) is already in the collection.</summary>
    public bool IsOwned => Owned.Any(c => c.IsSameCopyAs(Details));
}
