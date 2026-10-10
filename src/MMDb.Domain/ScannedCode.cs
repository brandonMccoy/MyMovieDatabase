using MMDb.Domain.Comics;

namespace MMDb.Domain;

public enum ProductKind
{
    Movie,
    Comic,
}

/// <summary>Tells what kind of product a scanned barcode is on, from the code alone.</summary>
public static class ScannedCode
{
    /// <summary>
    /// A book's ISBN (a trade or hardcover) or a UPC with an add-on (a comic's cover) is a comic; any other code
    /// is taken for a film. A comic whose add-on wasn't read looks like a film, so the user can say which it is.
    /// </summary>
    public static ProductKind KindOf(string? code) =>
        Isbn.TryParse(code, out _) || (ComicBarcode.TryParse(code, out var barcode) && barcode.Supplement is not null)
            ? ProductKind.Comic
            : ProductKind.Movie;
}
