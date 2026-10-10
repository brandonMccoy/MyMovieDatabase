using System.Globalization;
using System.Text.RegularExpressions;

namespace MMDb.Domain.Comics;

/// <summary>
/// Issue numbers are text: most are whole numbers, but there are #0, #½, #1.MU and #25AU. They are compared
/// by their leading number first ("#9" before "#10"), then by what follows.
/// </summary>
public static partial class IssueNumber
{
    /// <summary>Trims, drops a leading <c>#</c> and leading zeros (<c>#007</c> is <c>7</c>); <see langword="null"/> if blank.</summary>
    public static string? Normalize(string? number)
    {
        var text = number?.Trim().TrimStart('#').Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lead = LeadingZeros().Match(text);
        return lead.Success ? text[lead.Length..] : text;
    }

    /// <summary>Orders issue numbers the way they run: 0, ½, 1, 1.MU, 2, 10, Annual 1.</summary>
    public static IComparer<string?> Comparer { get; } = Comparer<string?>.Create(Compare);

    private static int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null || b is null)
        {
            return a is null ? 1 : -1;
        }

        var (numberA, restA) = Split(a);
        var (numberB, restB) = Split(b);
        var byNumber = (numberA ?? decimal.MaxValue).CompareTo(numberB ?? decimal.MaxValue);
        return byNumber != 0 ? byNumber : string.Compare(restA, restB, StringComparison.OrdinalIgnoreCase);
    }

    private static (decimal? Value, string Suffix) Split(string number)
    {
        if (number.StartsWith('½'))
        {
            return (0.5m, number[1..]);
        }

        var lead = LeadingNumber().Match(number);
        return lead.Success && decimal.TryParse(lead.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? (value, number[lead.Length..])
            : (null, number);
    }

    // "007" → "7", but "0" stays and "0.5" keeps its zero.
    [GeneratedRegex(@"^0+(?=\d)")]
    private static partial Regex LeadingZeros();

    [GeneratedRegex(@"^\d+(?:\.\d+)?")]
    private static partial Regex LeadingNumber();
}
