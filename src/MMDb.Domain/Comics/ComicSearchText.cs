using System.Globalization;
using System.Text.RegularExpressions;

namespace MMDb.Domain.Comics;

/// <summary>
/// What was typed into the comic search: a series, optionally the year it began, then an issue number.
/// "saga 7", "Saga #7", "Daredevil 1964 181", "Daredevil (1964) #181", "Batman".
/// </summary>
/// <remarks>A last word that is a year (1890–2100) after a series name is read as the year, not as an issue number.</remarks>
public sealed partial record ComicSearchText(string Series, int? SeriesYear, string? Number)
{
    public static ComicSearchText Parse(string? input)
    {
        var words = (input ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        string? number = null;
        if (words.Count > 1 && NumberWord().IsMatch(words[^1]) && !IsYear(words[^1]))
        {
            number = IssueNumber.Normalize(words[^1]);
            words.RemoveAt(words.Count - 1);
        }

        int? year = null;
        if (words.Count > 1 && IsYear(words[^1]))
        {
            year = int.Parse(words[^1].Trim('(', ')'), CultureInfo.InvariantCulture);
            words.RemoveAt(words.Count - 1);
        }

        return new(string.Join(' ', words).TrimEnd(','), year, number);
    }

    private static bool IsYear(string word) =>
        Year().IsMatch(word) && int.Parse(word.Trim('(', ')'), CultureInfo.InvariantCulture) is >= ComicDetails.MinYear and <= ComicDetails.MaxYear;

    // "#7", "181", "½", "1.MU", "25AU": starts with # or a digit.
    [GeneratedRegex(@"^(?:#\S+|\d\S*|½)$")]
    private static partial Regex NumberWord();

    [GeneratedRegex(@"^\(?\d{4}\)?$")]
    private static partial Regex Year();
}
