using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MMDb.Domain.Movies;

/// <summary>What a video file's name says about the film: "Blade.Runner.2049.2017.2160p.UHD.mkv" is "Blade Runner 2049", 2017.</summary>
/// <param name="Text">The name with the tags gone, year included if it has one: what to search for if the title and year find nothing.</param>
/// <remarks>
/// A number that looks like a year may belong to the title ("Blade Runner 2049.mkv"), so a search with
/// <see cref="Year"/> that finds nothing should be retried with <see cref="Text"/>.
/// </remarks>
public sealed record ReleaseName(string Text, string Title, int? Year)
{
    /// <summary>Whether a film's title is this one, ignoring case, accents and punctuation ("Amélie" is "amelie", "&amp;" is "and").</summary>
    public bool IsTitled(string? title) => title is not null && ReleaseNameParser.TitleKey(title) == ReleaseNameParser.TitleKey(Title);
}

/// <summary>
/// Reads film titles out of video file names by dropping quality, source, codec, language and group tags
/// ("2160p", "UHD", "WEB-DL", "x265", "KOREAN"…) and turning dots and underscores into spaces.
/// </summary>
public static partial class ReleaseNameParser
{
    /// <summary>The files a movie folder is searched for.</summary>
    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>([".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv"], StringComparer.OrdinalIgnoreCase);

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Reads a file by its place in the movie folder. A file whose own name gives no year takes the name of the
    /// folder it is in when that one does ("Heat (1995)/movie.mkv").
    /// </summary>
    /// <param name="relativePath">The file's path inside the movie folder.</param>
    public static ReleaseName ParsePath(string relativePath)
    {
        var name = Parse(Path.GetFileName(relativePath));
        if (name.Year is null && Path.GetFileName(Path.GetDirectoryName(relativePath)) is { Length: > 0 } folder
            && Parse(folder, isFile: false) is { Year: not null } fromFolder)
        {
            return fromFolder;
        }

        return name;
    }

    /// <param name="fileName">A file name, with or without its extension.</param>
    public static ReleaseName Parse(string fileName) => Parse(fileName, isFile: true);

    private static ReleaseName Parse(string fileName, bool isFile)
    {
        var original = (fileName ?? "").Trim();
        var name = isFile && IsVideoFile(original) ? Path.GetFileNameWithoutExtension(original) : original;

        // The title runs up to the first thing that can only be a tag, or to the year in brackets.
        var words = new List<string>();
        int? year = null;
        foreach (Match token in Tokens().Matches(name))
        {
            var word = token.Value;
            if (word[0] is '(' or '[' or '{')
            {
                // "(1995)" is the year; anything else in brackets is packaging or a group's name.
                var inner = word[1..^1].Trim();
                if (year is null && IsYear(inner))
                {
                    year = int.Parse(inner, CultureInfo.InvariantCulture);
                }

                if (words.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (!word.Any(char.IsLetterOrDigit))
            {
                continue;
            }

            if (words.Count > 0 && Tag().IsMatch(word))
            {
                break;
            }

            words.Add(word);
        }

        if (year is null)
        {
            // Without brackets the year is the last one before the tags, unless the name starts with it:
            // "1917.mkv" and "2001.A.Space.Odyssey.mkv" are titles.
            var yearAt = words.FindLastIndex(IsYear);
            if (yearAt > 0)
            {
                year = int.Parse(words[yearAt], CultureInfo.InvariantCulture);
                words.RemoveRange(yearAt, words.Count - yearAt);
            }
            else
            {
                // "The.Thing.Remastered.mkv": with no year to end the title, words that are usually tags do.
                while (words.Count > 1 && LooseTag().IsMatch(words[^1]))
                {
                    words.RemoveAt(words.Count - 1);
                }
            }
        }

        var title = string.Join(' ', words);
        if (title.Length == 0)
        {
            title = original;
        }

        return new(year is null ? title : string.Create(CultureInfo.InvariantCulture, $"{title} {year}"), title, year);
    }

    /// <summary>A title reduced to what two spellings of it share: lower case, no accents, no punctuation.</summary>
    internal static string TitleKey(string title)
    {
        var key = new StringBuilder(title.Length);
        foreach (var c in title.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
        }

        return key.ToString();
    }

    private static bool IsYear(string word) =>
        word.Length == 4 && int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= MovieDetails.MinYear and <= MovieDetails.MaxYear;

    // Words between dots, underscores and spaces; something in brackets is one token.
    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}|[^\s._()\[\]{}]+")]
    private static partial Regex Tokens();

    // What is never part of a title: resolutions, sources, codecs, episode numbers, with a group's name after a dash.
    [GeneratedRegex("""
        ^(?:
            \d{3,4}[pi] | uhd | hdr(?:10)?(?:\+|plus)? | dovi | sdr | 10bit | 8bit | hi10p?
          | blu-?ray | b[dr]-?rip | bd(?:25|50|remux) | remux | web-?(?:dl|rip) | hd-?(?:tv|rip|cam|ts) | dvd-?(?:rip|scr|r|5|9) | pdtv | camrip | telesync | vhsrip
          | [xh]26[45] | 26[45] | hevc | avc | xvid | divx | av1 | vp9
          | aac\d? | e?ac-?3 | dd[p+]?\d? | dts(?:-?(?:hd|ma|x|es))? | true-?hd | atmos | flac | mp3
          | s\d{1,2}(?:e\d{1,3})+ | \d{1,2}x\d{2,3}
          | amzn | dsnp | hmax | atvp
        )(?:-\w+)?$
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Tag();

    // Tags that are also words in titles ("The English Patient", "Uncut Gems"): only dropped from the end of a name with no year.
    [GeneratedRegex("""
        ^(?:
            [48]k | hd | sd | dv | 3d | web | dvd | bd | cam | ts | tc | scr | screener | opus | mpeg-?[24]?
          | proper | repack | rerip | limited | internal | extended | unrated | uncut | remastered | restored | theatrical | imax | criterion | retail | complete
          | multi | dual(?:-?audio)? | dubbed | subbed | subs? | (?:eng|ita|fre|fra|ger|deu|spa|esp|jpn|jap|kor|rus|chi|hin)(?:-?subs?)?
          | english | french | german | spanish | italian | japanese | korean | russian | chinese | mandarin | cantonese | hindi | swedish | danish | norwegian | polish | portuguese | vostfr | truefrench
          | cd\d | disc\d | part\d | nf | hulu | itunes
        )$
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex LooseTag();
}
