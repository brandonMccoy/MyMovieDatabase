using System.Globalization;

namespace MMDb.Domain.Wishlist;

/// <summary>
/// Where a wishlist item's Amazon button goes: a search of amazon.com for it. Only a link; Shelf itself asks
/// Amazon nothing.
/// </summary>
public static class AmazonSearch
{
    private const string Search = "https://www.amazon.com/s?k=";

    /// <summary>A search of Movies &amp; TV for the title, its year and the disc wanted: "Heat 1995 4K UHD".</summary>
    /// <returns><see langword="null"/> for a film wanted on Digital: there is no disc to look for.</returns>
    public static Uri? ForMovie(string title, int? year, MediaFormat format)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (!format.IsDisc())
        {
            return null;
        }

        var terms = new[] { title.Trim(), year?.ToString(CultureInfo.InvariantCulture), format.DisplayName() };
        return new Uri(Search + Uri.EscapeDataString(string.Join(' ', terms.Where(t => !string.IsNullOrEmpty(t)))) + "&i=movies-tv");
    }

    /// <summary>
    /// A search of all of Amazon for a comic by the name the wishlist shows it under: "Saga #7 comic" for an
    /// issue (the word keeps other things with the series' name out), "Saga Vol. 1" for a collected edition.
    /// </summary>
    public static Uri ForComic(string displayName, bool isSingleIssue)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return new Uri(Search + Uri.EscapeDataString(displayName.Trim() + (isSingleIssue ? " comic" : "")));
    }
}
