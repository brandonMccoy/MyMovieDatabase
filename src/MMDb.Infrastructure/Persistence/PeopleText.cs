using System.Globalization;
using System.Text;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

/// <summary>
/// The names of a film's cast and crew as one searchable column, so the library can find a film by anyone in
/// its credits without loading every film's credits. Text is folded (lower case, accents dropped) on both sides.
/// </summary>
internal static class PeopleText
{
    /// <summary>The shadow column on <c>Movies</c>; <see langword="null"/> until it has been filled in.</summary>
    public const string Column = "People";

    public static string Of(Movie movie) =>
        string.Join('\n', movie.Cast.Select(c => c.Name).Concat(movie.Crew.Select(c => c.Name)).Select(Fold).Distinct(StringComparer.Ordinal));

    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(c));
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
