using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MMDb.Infrastructure.Vault;

/// <summary>Just enough YAML to write note frontmatter that Obsidian and Claude read back exactly.</summary>
internal static partial class Yaml
{
    /// <summary>Plain when that reads back as the same text, double-quoted otherwise.</summary>
    public static string Text(string value) => IsPlain(value) ? value : Quoted(value);

    public static string Quoted(string value)
    {
        var quoted = new StringBuilder("\"", value.Length + 2);
        foreach (var c in value)
        {
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }

    // Starts with a letter or digit and holds only characters that mean nothing to YAML in this position.
    private static bool IsPlain(string value) =>
        PlainText().IsMatch(value)
        && !value.EndsWith(' ')
        && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        && !DateLike().IsMatch(value)
        && !Reserved.Contains(value);

    // Words YAML 1.1 (still used by many readers) turns into booleans or null.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "y", "n", "null", "nan", "inf",
    };

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} .,'()&!?/+\-]*$")]
    private static partial Regex PlainText();

    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}")]
    private static partial Regex DateLike();
}
