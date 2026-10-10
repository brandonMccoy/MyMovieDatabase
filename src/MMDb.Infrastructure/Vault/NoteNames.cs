using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MMDb.Infrastructure.Vault;

/// <summary>Note file names: "Heat (1995)", made safe for Windows, Android and Obsidian links.</summary>
internal static partial class NoteNames
{
    private const int MaxTitleLength = 120;

    // Not allowed in Windows file names, or breaks [[links]] in Obsidian.
    private const string Dropped = "*\"<>?|#^[]";

    public static string For(string title, int? year)
    {
        var safe = new StringBuilder(title.Length);
        foreach (var c in title.Trim())
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
        if (name.Length == 0)
        {
            name = "Untitled";
        }

        if (year is { } y)
        {
            return $"{name} ({y.ToString(CultureInfo.InvariantCulture)})";
        }

        return Reserved().IsMatch(name) ? name + " (film)" : name;
    }

    /// <summary>The <paramref name="n"/>th choice for a name already taken: "Heat (1995) (2)".</summary>
    public static string Numbered(string name, int n) => n == 1 ? name : $"{name} ({n.ToString(CultureInfo.InvariantCulture)})";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // Windows device names can't be file names, whatever the extension.
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
