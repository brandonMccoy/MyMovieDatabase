using System.Buffers;
using System.Text;

namespace MMDb.Infrastructure.Vault;

/// <summary>Writing collection text into Obsidian markdown without it turning into links, tags or formatting.</summary>
internal static class Markdown
{
    // Characters Obsidian reads as formatting, links, embeds, tags, comments, highlights or maths.
    private const string Special = @"\[]*_#<>`$%=~|";

    // A wikilink target can't hold these.
    private static readonly SearchValues<char> NotInLinks = SearchValues.Create(@"[]|#^\/:");

    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text.ReplaceLineEndings(" "))
        {
            if (Special.Contains(c, StringComparison.Ordinal))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary><c>[[Michael Mann]]</c>, or the plain name if it can't be a link target.</summary>
    public static string PersonLink(string name) =>
        name.AsSpan().IndexOfAny(NotInLinks) < 0 ? $"[[{name}]]" : Escape(name);

    /// <summary>A link that opens a file on this PC in its usual program.</summary>
    public static string FileLink(string path)
    {
        var url = new Uri(path).AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
        return $"[{Escape(Path.GetFileName(path))}]({url})";
    }
}
