namespace MMDb.Domain;

/// <summary>
/// A book's ISBN, as printed on a trade paperback or hardcover: ISBN-13 (978/979…) or the older ISBN-10, with a
/// valid check digit. Always held as ISBN-13. Spaces and hyphens are ignored when parsing.
/// </summary>
public sealed record Isbn
{
    private Isbn(string value) => Value = value;

    /// <summary>The ISBN-13, digits only.</summary>
    public string Value { get; }

    public static bool TryParse(string? input, out Isbn isbn)
    {
        isbn = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var code = input.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        if (code.Length == 13 && code.All(char.IsAsciiDigit) && (code.StartsWith("978", StringComparison.Ordinal) || code.StartsWith("979", StringComparison.Ordinal))
            && Upc.HasValidCheckDigit(code))
        {
            isbn = new Isbn(code);
            return true;
        }

        if (code.Length == 10 && code[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(code[9]) || code[9] == 'X') && IsValidIsbn10(code))
        {
            isbn = new Isbn(ToIsbn13(code));
            return true;
        }

        return false;
    }

    public static Isbn Parse(string input) =>
        TryParse(input, out var isbn) ? isbn : throw new FormatException($"'{input}' is not a valid ISBN.");

    public override string ToString() => Value;

    // Weights 10 down to 1; X is 10; the sum is a multiple of 11.
    private static bool IsValidIsbn10(string code)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += (10 - i) * (code[i] == 'X' ? 10 : code[i] - '0');
        }

        return sum % 11 == 0;
    }

    private static string ToIsbn13(string isbn10)
    {
        var body = "978" + isbn10[..9];
        for (var check = 0; check <= 9; check++)
        {
            var candidate = body + (char)('0' + check);
            if (Upc.HasValidCheckDigit(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unreachable: one check digit always fits.");
    }
}
