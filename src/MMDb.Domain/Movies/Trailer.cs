using System.Globalization;

namespace MMDb.Domain.Movies;

/// <summary>Where the Trailer button goes: the stored YouTube trailer, else a YouTube search.</summary>
public static class Trailer
{
    public static Uri UrlFor(string title, int? year, string? youTubeKey)
    {
        if (!string.IsNullOrWhiteSpace(youTubeKey))
        {
            return new Uri("https://www.youtube.com/watch?v=" + Uri.EscapeDataString(youTubeKey.Trim()));
        }

        var terms = new[] { title.Trim(), year?.ToString(CultureInfo.InvariantCulture), "trailer" };
        var query = string.Join(' ', terms.Where(t => !string.IsNullOrEmpty(t)));
        return new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query));
    }
}
