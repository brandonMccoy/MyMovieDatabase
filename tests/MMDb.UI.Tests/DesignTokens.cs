using System.Globalization;
using System.Text.RegularExpressions;

namespace MMDb.UI.Tests;

/// <summary>The custom properties declared in MMDb.UI's tokens.css.</summary>
internal sealed partial class DesignTokens
{
    private readonly Dictionary<string, string> _values;

    private DesignTokens(Dictionary<string, string> values) => _values = values;

    public static DesignTokens Load()
    {
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "tokens.css"));
        var withoutComments = Comment().Replace(css, "");
        var values = Declaration().Matches(withoutComments)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim());
        return new DesignTokens(values);
    }

    public string this[string name] =>
        _values.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"tokens.css does not define {name}");

    /// <summary>WCAG 2.x contrast ratio between two opaque hex colors (1 to 21).</summary>
    public double Contrast(string foreground, string background)
    {
        var a = RelativeLuminance(this[foreground]);
        var b = RelativeLuminance(this[background]);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        if (!HexColor().IsMatch(hex))
        {
            throw new FormatException($"Expected an opaque #rrggbb color, got '{hex}'");
        }

        double Channel(int offset)
        {
            var srgb = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?<name>--[\w-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex Declaration();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
