using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;

namespace MMDb.Contracts.Scans;

public enum ProductKind
{
    Movie,
    Comic,
}

/// <param name="Code">The digits read from the barcode; a comic's add-on follows its UPC.</param>
/// <param name="Kind">What the user said it is; <see langword="null"/> lets the PC tell from the code.</param>
public sealed record ScanRequest(string Code, ProductKind? Kind = null);

/// <summary>What a scanned barcode turned out to be, and what was found for it online. Nothing has been saved.</summary>
/// <param name="Kind">An ISBN or a UPC with a 5-digit add-on is a comic; any other code is a film.</param>
/// <param name="Movie">Set for <see cref="ProductKind.Movie"/>.</param>
/// <param name="Comic">Set for <see cref="ProductKind.Comic"/>.</param>
public sealed record ScanResponse(ProductKind Kind, MovieLookupResponse? Movie = null, ComicLookupResponse? Comic = null);
