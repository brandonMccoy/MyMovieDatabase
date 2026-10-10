namespace MMDb.Domain.Comics;

/// <summary>Field keys used in <see cref="ValidationError.Field"/> for comic input.</summary>
public static class ComicFields
{
    public const string Format = nameof(Format);
    public const string Series = nameof(Series);
    public const string SeriesYear = nameof(SeriesYear);
    public const string Number = nameof(Number);
    public const string Title = nameof(Title);
    public const string Variant = nameof(Variant);
    public const string Printing = nameof(Printing);
    public const string Publisher = nameof(Publisher);
    public const string Writers = nameof(Writers);
    public const string Artists = nameof(Artists);
    public const string Barcode = nameof(Barcode);
    public const string Isbn = nameof(Isbn);
    public const string KeyNote = nameof(KeyNote);
    public const string CoverUrl = nameof(CoverUrl);
}
