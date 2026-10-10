using System.Globalization;
using MMDb.Contracts.Comics;

namespace MMDb.UI.Components;

/// <summary>How comics read on screen: formats, copies, cover dates.</summary>
public static class ComicText
{
    public static IReadOnlyList<ComicFormatKind> Formats { get; } = Enum.GetValues<ComicFormatKind>();

    public static string Name(ComicFormatKind format) => format switch
    {
        ComicFormatKind.SingleIssue => "Single issue",
        ComicFormatKind.TradePaperback => "Trade paperback",
        ComicFormatKind.Hardcover => "Hardcover",
        ComicFormatKind.Omnibus => "Omnibus",
        _ => format.ToString(),
    };

    public static bool IsCollected(ComicFormatKind format) => format != ComicFormatKind.SingleIssue;

    /// <summary>"Cover B", "Cover B · 2nd printing", "2nd printing"; empty for a regular first printing.</summary>
    public static string Copy(string? variant, int? printing) => string.Join(" · ", new[]
    {
        variant,
        printing is > 1 and var p ? $"{Ordinal(p)} printing" : null,
    }.OfType<string>());

    /// <summary>The line under a tile: why it's a key issue, or which copy it is.</summary>
    public static string Caption(ComicSummaryDto comic) =>
        comic.IsKeyIssue && comic.KeyNote is { } note ? note : Copy(comic.Variant, comic.Printing);

    public static string MonthYear(DateOnly date) => date.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    public static string LongMonthYear(DateOnly date) => date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Ordinal(int n) => n.ToString(CultureInfo.InvariantCulture) + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th",
    });
}
