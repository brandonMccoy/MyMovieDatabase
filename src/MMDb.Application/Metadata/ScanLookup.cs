using MMDb.Domain;

namespace MMDb.Application.Metadata;

/// <param name="Movie">Set for <see cref="ProductKind.Movie"/>.</param>
/// <param name="Comic">Set for <see cref="ProductKind.Comic"/>.</param>
public sealed record ScanResult(ProductKind Kind, MovieLookupResult? Movie, ComicLookupResult? Comic);

/// <summary>Looks a scanned barcode up as a film or as a comic: what the user said it is, else what the code says.</summary>
public sealed class ScanLookup(MovieLookup movies, ComicLookup comics)
{
    public async Task<ScanResult> LookupAsync(string code, ProductKind? kind = null, CancellationToken cancellationToken = default) =>
        (kind ?? ScannedCode.KindOf(code)) == ProductKind.Comic
            ? new(ProductKind.Comic, null, await comics.LookupAsync(code, cancellationToken))
            : new(ProductKind.Movie, await movies.LookupAsync(code, cancellationToken), null);
}
