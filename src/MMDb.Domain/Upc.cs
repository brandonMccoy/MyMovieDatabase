namespace MMDb.Domain;

/// <summary>
/// A retail barcode as printed on a movie case: UPC-A (12 digits) or EAN-13 (13 digits),
/// with a valid GS1 check digit. Spaces and hyphens are ignored when parsing.
/// </summary>
public sealed record Upc
{
    private Upc(string value) => Value = value;

    /// <summary>Digits only, no separators.</summary>
    public string Value { get; }

    public static bool TryParse(string? input, out Upc upc)
    {
        upc = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = input.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (digits.Length is not (12 or 13) || !digits.All(char.IsAsciiDigit) || !HasValidCheckDigit(digits))
        {
            return false;
        }

        upc = new Upc(digits);
        return true;
    }

    public static Upc Parse(string input) =>
        TryParse(input, out var upc) ? upc : throw new FormatException($"'{input}' is not a valid UPC-A or EAN-13 barcode.");

    /// <summary>
    /// The same barcode written the other way: a UPC-A is the EAN-13 with a leading 0, so <c>024543617907</c>
    /// and <c>0024543617907</c> are one product. <see langword="null"/> for EAN-13s not starting with 0.
    /// </summary>
    public string? AlternateForm => Value.Length == 12 ? "0" + Value : Value[0] == '0' ? Value[1..] : null;

    public override string ToString() => Value;

    // GS1: weight the digits left of the check digit 3,1,3,1… starting from the right.
    internal static bool HasValidCheckDigit(string digits)
    {
        var sum = 0;
        for (int i = digits.Length - 2, weight = 3; i >= 0; i--, weight = 4 - weight)
        {
            sum += (digits[i] - '0') * weight;
        }

        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }
}
