using System.Globalization;

namespace MMDb.UI.Stats;

/// <summary>One bar of a chart.</summary>
/// <param name="Tip">What hovering it says, and what a screen reader reads: "1990s · 212 films".</param>
/// <param name="Href">Where clicking it goes; <see langword="null"/> for a bar that opens nothing.</param>
public sealed record ChartBar(string Label, int Value, string Tip, string? Href = null);

public static class ChartAxis
{
    private static readonly double[] Steps = [1, 1.2, 1.6, 2, 2.4, 3, 4, 5, 6, 8, 10];

    /// <summary>
    /// The value at the top of a chart whose tallest bar is <paramref name="max"/>: a round number at or above
    /// it, and even, so the gridline halfway up is a whole number too.
    /// </summary>
    public static int Top(int max)
    {
        if (max <= 10)
        {
            return Math.Max(2, max + max % 2);
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        return (int)Math.Round(Steps.First(step => step * magnitude >= max) * magnitude);
    }

    /// <summary>A CSS percentage: how far up (or along) a chart <paramref name="value"/> reaches.</summary>
    public static string Percent(int value, int top) =>
        (top <= 0 ? 0 : value * 100.0 / top).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

public static class FileSize
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    /// <summary>"1.9 TB", "214 GB": as Windows counts (1 KB = 1,024 bytes), one decimal below ten.</summary>
    public static string Text(long bytes)
    {
        double size = bytes;
        var unit = "bytes";
        foreach (var next in Units)
        {
            if (size < 1024)
            {
                break;
            }

            size /= 1024;
            unit = next;
        }

        var format = unit == "bytes" || Math.Round(size, 1) >= 10 ? "N0" : "0.#";
        return size.ToString(format, CultureInfo.InvariantCulture) + " " + unit;
    }
}
