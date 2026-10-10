using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MMDb.Infrastructure.Vault;

/// <summary>Note and folder names: "Heat (1995)", "Saga 7", made safe for Windows, Android and Obsidian links.</summary>
internal static partial class NoteNames
{
    private const int MaxTitleLength = 120;

    // Not allowed in Windows file names, or breaks [[links]] in Obsidian.
    private const string Dropped = "*\"<>?|#^[]";

    public static string For(string title, int? year)
    {
        var name = Clean(title);
        if (year is { } y)
        {
            return $"{name} ({y.ToString(CultureInfo.InvariantCulture)})";
        }

        return Reserved().IsMatch(name) ? name + " (film)" : name;
    }

    /// <summary>"Saga 7", "Saga 7 (Cover B)", "Saga 7 (2nd printing)": <c>#</c> can't be in a link, so it is left out.</summary>
    public static string ForIssue(string series, string number, string? variant, int? printing)
    {
        var name = $"{Clean(series)} {Clean(number)}";
        if (variant is not null)
        {
            name += $" ({Clean(variant)})";
        }

        if (printing is > 1 and var p)
        {
            name += $" ({Ordinal(p)} printing)";
        }

        return name;
    }

    /// <summary>Text made safe as (part of) a file name; "Untitled" if nothing is left.</summary>
    public static string Clean(string text)
    {
        var safe = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (c == ':')
            {
                safe.Append(" -");
            }
            else if (c is '/' or '\\')
            {
                safe.Append('-');
            }
            else if (char.IsWhiteSpace(c))
            {
                safe.Append(' ');
            }
            else if (!char.IsControl(c) && !Dropped.Contains(c, StringComparison.Ordinal))
            {
                safe.Append(c);
            }
        }

        var name = Spaces().Replace(safe.ToString(), " ").Trim().TrimStart('.').Trim();
        if (name.Length > MaxTitleLength)
        {
            name = name[..MaxTitleLength].TrimEnd();
        }

        name = name.TrimEnd('.', ' ');
        return name.Length == 0 ? "Untitled" : name;
    }

    public static string Ordinal(int n) => n.ToString(CultureInfo.InvariantCulture) + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th",
    });

    /// <summary>The <paramref name="n"/>th choice for a name already taken: "Heat (1995) (2)".</summary>
    public static string Numbered(string name, int n) => n == 1 ? name : $"{name} ({n.ToString(CultureInfo.InvariantCulture)})";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // Windows device names can't be file names, whatever the extension.
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
