using System.Text.RegularExpressions;

namespace MMDb.Domain.Movies;

/// <summary>
/// A film as a barcode database names the product, e.g. "Heat [Blu-ray] (Widescreen)": the title with the
/// packaging words removed, plus the format and year when the product name gives them.
/// </summary>
public sealed partial record RetailTitle(string Title, int? Year, MediaFormat? Format)
{
    public static RetailTitle Parse(string productTitle)
    {
        var original = productTitle?.Trim() ?? "";
        int? year = null;

        // Anything in brackets is packaging ("[Blu-ray]", "(Widescreen)"), except that "(1995)" is the year.
        var text = Bracketed().Replace(original, match =>
        {
            if (year is null && int.TryParse(match.Groups["inner"].ValueSpan.Trim(), out var y) && y is >= MovieDetails.MinYear and <= MovieDetails.MaxYear)
            {
                year = y;
            }

            return " ";
        });
        text = LabelPrefix().Replace(text, "");
        text = Packaging().Replace(text, " ");
        text = Spaces().Replace(text, " ").Trim(' ', '-', '–', ':', ',', '/', '+', '&');

        return new(text.Length > 0 ? text : original, year, DetectFormat(original));
    }

    // A combo pack is listed under its best disc: "4K UHD + Blu-ray" is a 4K UHD.
    private static MediaFormat? DetectFormat(string productTitle) =>
        Uhd().IsMatch(productTitle) ? MediaFormat.Uhd4K
        : BluRay().IsMatch(productTitle) ? MediaFormat.BluRay
        : Dvd().IsMatch(productTitle) ? MediaFormat.Dvd
        : null;

    [GeneratedRegex(@"[\[(](?<inner>[^\[\]()]*)[\])]")]
    private static partial Regex Bracketed();

    // Home-video labels that put their name in front: "Mill Creek - Fight Club".
    [GeneratedRegex(@"^\s*(?:Mill\s+Creek|Criterion(?:\s+Collection)?|Arrow(?:\s+Video)?|Shout!?(?:\s+Factory)?|Kino\s+Lorber|Warner\s+Archive)\s*[-–:]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefix();

    [GeneratedRegex("""
        \b(?:
            4K(?:\s+Ultra\s*HD)? | Ultra\s*HD | UHD | Blu[\s-]?ray(?:\s*3D)? | BD | DVD | Digital(?:\s+(?:HD|Copy|Code))?
          | Combo(?:\s+Pack)? | \d+[\s-]?Discs?(?:\s+Set)?
          | Wide\s*screen | Full\s*screen | Steelbook | Remastered | Restored | Region\s+(?:\d|[A-C]|Free)
          | (?:\d+(?:st|nd|rd|th)\s+)?(?:Special|Collector['’]?s|Limited|Deluxe|Ultimate|Anniversary|Definitive|Director['’]?s|Extended|Theatrical|Unrated)\s+(?:Edition|Cut|Version)
        )\b
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Packaging();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b(?:4K|UHD|Ultra\s*HD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Uhd();

    [GeneratedRegex(@"\b(?:Blu[\s-]?ray|BD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BluRay();

    [GeneratedRegex(@"\bDVD\b", RegexOptions.IgnoreCase)]
    private static partial Regex Dvd();
}
