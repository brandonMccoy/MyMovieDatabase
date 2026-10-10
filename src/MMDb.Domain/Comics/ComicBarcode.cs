using System.Globalization;

namespace MMDb.Domain.Comics;

/// <summary>
/// The barcode on a comic's cover: a UPC-A (or EAN-13) and, printed to its right, a 2- or 5-digit add-on.
/// Every issue of a series shares the UPC; the add-on tells the issue, cover and printing apart.
/// Typed or scanned as one run of digits (<c>75960620068700711</c>); spaces and hyphens are ignored.
/// </summary>
public sealed record ComicBarcode
{
    private ComicBarcode(Upc upc, string? supplement)
    {
        Upc = upc;
        Supplement = supplement;
    }

    public Upc Upc { get; }

    /// <summary>The add-on digits, if the barcode has them.</summary>
    public string? Supplement { get; }

    /// <summary>The UPC and add-on as one run of digits, the way Metron stores it.</summary>
    public string Value => Upc.Value + Supplement;

    /// <summary>The 5-digit add-on read the way Marvel, DC and most US publishers print it: issue, cover, printing.</summary>
    public ComicAddOn? AddOn => Supplement is { Length: 5 } s
        ? new(int.Parse(s.AsSpan(0, 3), CultureInfo.InvariantCulture), s[3] - '0', s[4] - '0')
        : null;

    public static bool TryParse(string? input, out ComicBarcode barcode)
    {
        barcode = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = input.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

        // Lengths can't be mistaken for one another: 12 + 2 = 14, 13 + 2 = 15, 12 + 5 = 17, 13 + 5 = 18.
        int? length = digits.Length switch
        {
            12 or 13 => digits.Length,
            14 or 17 => 12,
            15 or 18 => 13,
            _ => null,
        };

        if (!digits.All(char.IsAsciiDigit) || length is not { } upcLength || !Upc.TryParse(digits[..upcLength], out var upc))
        {
            return false;
        }

        var rest = digits[upcLength..];
        barcode = new ComicBarcode(upc, rest.Length == 0 ? null : rest);
        return true;
    }

    public static ComicBarcode Parse(string input) =>
        TryParse(input, out var barcode) ? barcode : throw new FormatException($"'{input}' is not a valid comic barcode.");

    /// <summary>The UPC, a space and the add-on: how it reads under the bars.</summary>
    public override string ToString() => Supplement is null ? Upc.Value : $"{Upc.Value} {Supplement}";
}

/// <param name="Issue">The issue number (<c>007</c> is 7).</param>
/// <param name="Cover">Which cover: 1 is the regular one, higher digits are variants.</param>
/// <param name="Printing">1 for the first printing.</param>
public sealed record ComicAddOn(int Issue, int Cover, int Printing);
