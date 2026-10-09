using System.Text.RegularExpressions;

namespace MMDb.Domain.Movies;

/// <summary>What was typed into the movie search: a title, optionally ending in a year ("heat 1995", "Heat (1995)").</summary>
/// <remarks>
/// A trailing year may belong to the title ("Blade Runner 2049"), so a search with <see cref="Year"/> that finds
/// nothing should be retried with <see cref="Text"/>.
/// </remarks>
public sealed partial record SearchText(string Text, string Title, int? Year)
{
    public static SearchText Parse(string? input)
    {
        var text = Spaces().Replace(input ?? "", " ").Trim();
        var match = TrailingYear().Match(text);
        return match.Success && int.Parse(match.Groups["year"].ValueSpan, System.Globalization.CultureInfo.InvariantCulture) is var year
            && year is >= MovieDetails.MinYear and <= MovieDetails.MaxYear
            ? new(text, match.Groups["title"].Value.TrimEnd(' ', ','), year)
            : new(text, text, null);
    }

    [GeneratedRegex(@"^(?<title>.*\S)[\s,]+\(?(?<year>\d{4})\)?$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
