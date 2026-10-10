using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Vault;

/// <summary>A film's note: frontmatter with every field, a readable body, and the user's own "My notes".</summary>
internal static partial class MovieNote
{
    public const string ManagedBy = "mmdb";
    public const string MyNotesHeading = "## My notes";
    public const string CrewHeading = "## Crew";

    /// <summary>How many of the cast the frontmatter lists; the note itself links every one of them.</summary>
    public const int TopBilled = 10;

    // The crew that get a line of links like the directors, and a frontmatter list.
    private static readonly (string Label, string Key, IReadOnlyList<string> Jobs)[] Headline =
    [
        ("Written by", "writers", CrewCredits.WritingJobs),
        ("Produced by", "producers", [CrewCredits.Producer]),
        ("Executive producers", "executive_producers", [CrewCredits.ExecutiveProducer]),
        ("Cinematography", "cinematographers", [CrewCredits.DirectorOfPhotography]),
        ("Music", "composers", [CrewCredits.Composer]),
        ("Edited by", "editors", [CrewCredits.Editor]),
    ];

    /// <param name="cover">The cover's path in the vault, if it has one there.</param>
    /// <param name="myNotes">What follows the My notes heading in the note being replaced (see <see cref="MyNotesOf"/>).</param>
    public static string Render(Movie movie, string? cover, string myNotes, TimeZoneInfo timeZone)
    {
        var note = new StringBuilder();
        note.Append("---\n");
        Key(note, "mmdb_id", movie.Id.ToString("D"));
        Key(note, "managed_by", ManagedBy);
        Key(note, "title", Yaml.Text(movie.Title));
        Key(note, "year", movie.Year);
        List(note, "formats", movie.Formats.Select(f => f.Format.DisplayName()));
        List(note, "directors", movie.Directors);
        foreach (var (_, key, jobs) in Headline)
        {
            List(note, key, CrewCredits.NamesFor(movie.Crew, jobs));
        }

        List(note, "cast", movie.Cast.Take(TopBilled).Select(c => c.Name));
        List(note, "genres", movie.Genres);
        Key(note, "runtime", movie.RuntimeMinutes);
        Key(note, "rating", movie.Rating is { } rating ? Yaml.Text(rating) : null);
        Key(note, "tmdb_id", movie.TmdbId);
        Key(note, "trailer", movie.TrailerYouTubeKey is null ? null : Yaml.Text(Trailer.UrlFor(movie.Title, movie.Year, movie.TrailerYouTubeKey).AbsoluteUri));
        Key(note, "cover", cover is null ? null : Yaml.Quoted($"[[{cover}]]"));
        Key(note, "added", TimeZoneInfo.ConvertTime(movie.CreatedAt, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        note.Append("---\n");

        note.Append("# ").Append(Markdown.Escape(movie.Title));
        if (movie.Year is { } year)
        {
            note.Append(" (").Append(year.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        note.Append('\n');
        if (cover is not null)
        {
            note.Append("![[").Append(cover).Append("|200]]\n");
        }

        note.Append('\n');
        if (!string.IsNullOrWhiteSpace(movie.Overview))
        {
            note.Append(Markdown.Escape(movie.Overview.Trim())).Append("\n\n");
        }

        if (movie.Directors.Count > 0)
        {
            note.Append("**Directed by** ").AppendJoin(", ", movie.Directors.Select(Markdown.PersonLink)).Append('\n');
        }

        foreach (var (label, _, jobs) in Headline)
        {
            if (CrewCredits.NamesFor(movie.Crew, jobs) is { Count: > 0 } names)
            {
                note.Append("**").Append(label).Append("** ").AppendJoin(", ", names.Select(Markdown.PersonLink)).Append('\n');
            }
        }

        if (movie.Cast.Count > 0)
        {
            note.Append("**Cast** ").AppendJoin(", ", movie.Cast.Select(c => Markdown.PersonLink(c.Name))).Append('\n');
        }

        note.Append("**Owned on** ").AppendJoin(", ", movie.Formats.Select(Owned)).Append('\n');
        note.Append("[Trailer](").Append(Trailer.UrlFor(movie.Title, movie.Year, movie.TrailerYouTubeKey).AbsoluteUri).Append(')');
        if (movie.TmdbId is { } tmdbId)
        {
            note.Append(" · [TMDB](https://www.themoviedb.org/movie/").Append(tmdbId.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        note.Append("\n\n");
        Crew(note, movie);
        note.Append("%% Shelf rewrites everything above \"My notes\" when the film changes. Write your own notes below it. %%\n");
        note.Append(MyNotesHeading).Append('\n');
        note.Append(myNotes);
        return note.ToString();
    }

    /// <summary>Everything after the My notes heading, exactly as written; empty if there is no such heading.</summary>
    public static string MyNotesOf(string note)
    {
        var heading = MyNotesLine().Match(note);
        return heading.Success ? note[(heading.Index + heading.Length)..] : "";
    }

    public static bool HasMyNotes(string note) => !string.IsNullOrWhiteSpace(MyNotesOf(note));

    /// <summary>The film a note belongs to, if Shelf wrote it (frontmatter has <c>managed_by: mmdb</c> and an id).</summary>
    public static Guid? IdOf(string note)
    {
        var frontmatter = Frontmatter().Match(note);
        if (!frontmatter.Success)
        {
            return null;
        }

        var block = frontmatter.Groups["yaml"].Value;
        if (!ManagedLine().IsMatch(block))
        {
            return null;
        }

        var id = IdLine().Match(block);
        return id.Success && Guid.TryParse(id.Groups["id"].Value, out var parsed) ? parsed : null;
    }

    // Everyone the headline lines above don't name: a line per job title, in credit order, each person a link.
    private static void Crew(StringBuilder note, Movie movie)
    {
        var jobs = movie.Crew.Where(c => !CrewCredits.IsHeadline(c)).GroupBy(c => c.Job, StringComparer.OrdinalIgnoreCase).ToList();
        if (jobs.Count == 0)
        {
            return;
        }

        note.Append(CrewHeading).Append('\n');
        foreach (var job in jobs)
        {
            note.Append("**").Append(Markdown.Escape(job.Key)).Append("** ")
                .AppendJoin(", ", job.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(Markdown.PersonLink))
                .Append('\n');
        }

        note.Append('\n');
    }

    private static string Owned(MovieFormat format)
    {
        var name = format.Format.DisplayName();
        if (format.Upc is { } upc)
        {
            return $"{name} (UPC {upc.Value})";
        }

        return format.FilePath is { } path ? $"{name} ({Markdown.FileLink(path)})" : name;
    }

    internal static void Key(StringBuilder note, string key, string? value)
    {
        if (value is not null)
        {
            note.Append(key).Append(": ").Append(value).Append('\n');
        }
    }

    internal static void Key(StringBuilder note, string key, int? value) =>
        Key(note, key, value?.ToString(CultureInfo.InvariantCulture));

    internal static void List(StringBuilder note, string key, IEnumerable<string> values)
    {
        var items = values.ToList();
        if (items.Count == 0)
        {
            return;
        }

        note.Append(key).Append(":\n");
        foreach (var item in items)
        {
            note.Append("  - ").Append(Yaml.Text(item)).Append('\n');
        }
    }

    [GeneratedRegex(@"\A---\r?\n(?<yaml>.*?)\r?\n---\r?$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"^managed_by:\s*[""']?mmdb[""']?\s*$", RegexOptions.Multiline)]
    private static partial Regex ManagedLine();

    [GeneratedRegex(@"^mmdb_id:\s*[""']?(?<id>[0-9a-fA-F-]{32,36})[""']?\s*$", RegexOptions.Multiline)]
    private static partial Regex IdLine();

    [GeneratedRegex(@"^## My notes[ \t]*(?:\r?\n|\z)", RegexOptions.Multiline)]
    private static partial Regex MyNotesLine();
}
