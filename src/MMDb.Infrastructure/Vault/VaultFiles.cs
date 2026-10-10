using System.Globalization;
using System.Text;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Vault;

/// <summary>The vault's shared files: the index, the guide for Claude, and the Bases views.</summary>
internal static class VaultFiles
{
    public const string Index = "_Index.md";
    public const string Guide = "CLAUDE.md";
    public const string MoviesBase = "Movies.base";
    public const string ComicsBase = "Comics.base";

    /// <summary>Files Shelf may rewrite carry this; a version without it (the user's own) is left alone.</summary>
    public static bool IsManaged(string name, string text) => name switch
    {
        Guide => text.StartsWith(GuideMarker, StringComparison.Ordinal),
        MoviesBase or ComicsBase => text.StartsWith(BaseMarker, StringComparison.Ordinal),
        _ => text.StartsWith("---\nmanaged_by: mmdb\n", StringComparison.Ordinal),
    };

    /// <param name="movies">Each film with its note's name (no folder or extension).</param>
    /// <param name="series">Each run of single issues, with its series note's path (no extension).</param>
    /// <param name="collected">Each trade, hardcover and omnibus with its note's path (no extension).</param>
    public static string RenderIndex(
        IReadOnlyList<(Movie Movie, string Note)> movies,
        IReadOnlyList<SeriesIndexEntry> series,
        IReadOnlyList<(Comic Comic, string Note)> collected)
    {
        var index = new StringBuilder();
        index.Append("---\nmanaged_by: mmdb\n---\n");
        index.Append("# Collection index\n\n");
        index.Append("Shelf writes this file. Movies: one line per film (note · directors · formats owned). ");
        index.Append("Comics: one line per series (series note · publisher · issues owned · missing · key issues), then one line per trade, hardcover or omnibus.\n\n");

        var count = movies.Count;
        index.Append("**Movies:** ").Append(Count(count)).Append(count == 1 ? " film" : " films");
        foreach (var format in Enum.GetValues<MediaFormat>())
        {
            var owned = movies.Count(n => n.Movie.Owns(format));
            if (owned > 0)
            {
                index.Append(" · ").Append(format.DisplayName()).Append(' ').Append(Count(owned));
            }
        }

        var issues = series.Sum(s => s.Copies.Count);
        index.Append("\n**Comics:** ").Append(Count(issues)).Append(issues == 1 ? " issue" : " issues")
            .Append(" · ").Append(Count(series.Count)).Append(" series")
            .Append(" · ").Append(Count(collected.Count)).Append(collected.Count == 1 ? " trade or hardcover" : " trades and hardcovers");

        index.Append("\n\n## Movies\n");
        foreach (var (movie, note) in movies.OrderBy(n => n.Note, StringComparer.InvariantCultureIgnoreCase).ThenBy(n => n.Note, StringComparer.Ordinal))
        {
            index.Append("- [[").Append(note).Append("]]");
            if (movie.Directors.Count > 0)
            {
                index.Append(" · ").AppendJoin(", ", movie.Directors.Select(Markdown.Escape));
            }

            index.Append(" · ").AppendJoin(", ", movie.Formats.Select(f => f.Format.DisplayName())).Append('\n');
        }

        if (series.Count > 0)
        {
            index.Append("\n## Comics\n");
            foreach (var run in series.OrderBy(s => s.Title, StringComparer.InvariantCultureIgnoreCase))
            {
                index.Append("- ").Append(SeriesLine(run)).Append('\n');
            }
        }

        if (collected.Count > 0)
        {
            index.Append("\n## Trades and hardcovers\n");
            foreach (var (comic, note) in collected.OrderBy(c => c.Comic.DisplayName, StringComparer.InvariantCultureIgnoreCase))
            {
                index.Append("- [[").Append(note).Append('|').Append(comic.DisplayName.Replace("|", "-", StringComparison.Ordinal).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal)).Append("]]")
                    .Append(" · ").Append(comic.Format.DisplayName());
                if (comic.Publisher is { } publisher)
                {
                    index.Append(" · ").Append(Markdown.Escape(publisher));
                }

                if (comic.Isbn is { } isbn)
                {
                    index.Append(" · ISBN ").Append(isbn.Value);
                }

                index.Append('\n');
            }
        }

        return index.ToString();
    }

    private static string SeriesLine(SeriesIndexEntry run)
    {
        var numbers = run.Copies.Select(c => c.Number!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = run.Facts?.IssueNumbers ?? [];
        IReadOnlyList<string> order = known.Count > 0 ? known : [.. numbers.Order(IssueNumber.Comparer)];
        var parts = new List<string> { $"[[{run.SeriesNote}|{run.Title.Replace("|", "-", StringComparison.Ordinal).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal)}]]" };
        if ((run.Copies.Select(c => c.Publisher).OfType<string>().FirstOrDefault() ?? run.Facts?.Publisher) is { } publisher)
        {
            parts.Add(Markdown.Escape(publisher));
        }

        var missing = known.Where(n => !numbers.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        parts.Add(known.Count > 0 ? $"{Count(known.Count - missing.Count)} of {Count(known.Count)}" : $"{Count(numbers.Count)} {(numbers.Count == 1 ? "issue" : "issues")}");
        parts.Add(Markdown.Escape("owned " + IssueRanges.Format(numbers, order)));
        if (missing.Count > 0)
        {
            parts.Add(Markdown.Escape("missing " + IssueRanges.Format(missing, known)));
        }

        var keys = run.Copies
            .Where(c => c.IsKeyIssue)
            .GroupBy(c => c.Number!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, IssueNumber.Comparer)
            .Select(g => g.Select(c => c.KeyNote).OfType<string>().FirstOrDefault() is { } note ? $"#{g.Key} {note}" : $"#{g.Key}")
            .ToList();
        if (keys.Count > 0)
        {
            parts.Add(Markdown.Escape("key " + string.Join(", ", keys)));
        }

        return string.Join(" · ", parts);
    }

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private const string GuideMarker = "<!-- managed_by: mmdb";

    // Line endings normalized: a checkout may have turned this file's into CRLF.
    public static readonly string GuideText = (GuideMarker + """
         · Shelf rewrites this file. Delete this line to keep your own version. -->
        # Shelf collection vault

        This vault mirrors a physical collection of movies and comics kept in the Shelf app. Shelf writes these files and never reads them back, so changing them here doesn't change the collection.

        ## Answering questions
        - Start with `_Index.md`: the totals, then one line per film (note · directors · formats owned), one line per comic series (publisher · issues owned · missing · key issues) and one per trade or hardcover. "Do I own…", "how many 4K discs", "which Daredevil issues am I missing" or "which Carpenter films" can be answered from it alone.
        - Open a film's note in `Movies/` only for what the index doesn't have: cast, genres, runtime, rating, overview, barcodes, file paths, the user's own notes.
        - A series' `_Series.md` lists every issue owned (each copy linked) and every issue missing. Open an issue's note for its creators, cover date, variant, barcode, summary and the user's notes.
        - To filter many notes on one field, search the frontmatter of `Movies/*.md` or `Comics/**/*.md` instead of opening the notes one by one.

        ## Layout
        - `Movies/<Title> (<Year>).md`: one note per film, however many formats it is owned on. Characters a file name can't hold are dropped or replaced (`:` becomes ` -`); a second film with the same name gets ` (2)`.
        - `Comics/<Series> (<Year it began>)/<Series> <Issue>.md`: one note per comic copy, so a variant cover owned as well has its own note (`Saga 7 (Cover B).md`). File names leave out `#`, which can't be in a link; headings keep it.
        - `Comics/<Series> (<Year it began>)/_Series.md`: the series, its issues owned and missing.
        - `Comics/Trades & HCs/<Title>.md`: trades, hardcovers and omnibuses.
        - `Attachments/covers/`: cover images.
        - `Movies.base`, `Comics.base`: Obsidian Bases views (covers, tables, recently added, key issues).

        ## Movie note frontmatter
        | Key | Type | Meaning |
        |---|---|---|
        | `mmdb_id` | text | Shelf's id for the film; it never changes |
        | `managed_by` | text | always `mmdb`: Shelf writes this note |
        | `title` | text | |
        | `year` | number | release year |
        | `formats` | list | formats owned: `4K UHD`, `Blu-ray`, `DVD`, `Digital` |
        | `directors` | list | |
        | `cast` | list | top-billed actors, in billing order |
        | `genres` | list | |
        | `runtime` | number | minutes |
        | `rating` | text | US certification, e.g. `R`, `PG-13` |
        | `tmdb_id` | number | The Movie Database id |
        | `trailer` | text | YouTube link to the official trailer |
        | `cover` | link | the cover image |
        | `added` | date | when the film was first added to the collection |

        ## Comic note frontmatter
        | Key | Type | Meaning |
        |---|---|---|
        | `mmdb_id` | text | Shelf's id for the copy; it never changes |
        | `managed_by` | text | always `mmdb` |
        | `series` | text | series name |
        | `volume` | number | the year the series began, which tells volumes apart: Daredevil (1964), Daredevil (1998) |
        | `issue` | text | issue number (`"181"`, `"½"`, `"1.MU"`); for a trade or hardcover, its volume number |
        | `title` | text | a trade's title, or an issue's story title |
        | `variant` | text | which cover, e.g. `Cover B` |
        | `printing` | number | `1` for a first printing |
        | `publisher` | text | |
        | `cover_date` | text | `YYYY-MM`, the date printed on the cover |
        | `format` | text | `single issue`, `trade paperback`, `hardcover` or `omnibus` |
        | `writers` | list | |
        | `artists` | list | pencils, inks, colors and covers |
        | `key_issue` | boolean | `true` for a key issue (first appearances, deaths…); left out otherwise |
        | `key_note` | text | why it is a key issue, e.g. `1st Elektra` |
        | `upc` | text | the cover barcode with its 5-digit add-on (issue, cover, printing) |
        | `isbn` | text | a trade's or hardcover's ISBN-13 |
        | `metron_id` | number | Metron issue id |
        | `cover` | link | the cover image |
        | `added` | date | when the copy was added to the collection |

        `_Series.md` frontmatter: `series`, `volume`, `publisher`, `owned` (issue numbers owned; variants count once), `issues` (issues in the series according to Metron, if known), `metron_series_id`.

        Keys without a value are left out. Note bodies repeat the details with `[[links]]` to people (and from an issue to its series), the barcode of each disc or comic, and a link to the file of a digital copy.

        ## Editing
        - Everything above `## My notes` is rewritten whenever the film or comic changes in Shelf. Text under `## My notes` is kept. `_Series.md` has no My notes: Shelf rewrites all of it.
        - To change the collection, use Shelf; new or renamed files in `Movies/` and `Comics/` are not read.
        - When a film or comic is removed from Shelf its note is deleted, or moved to `.trash/` if it had text under `## My notes`.
        - Files without `managed_by: mmdb` are never touched.

        """).ReplaceLineEndings("\n");

    private const string BaseMarker = "# managed_by: mmdb";

    public static readonly string MoviesBaseText = (BaseMarker + """
         · Shelf rewrites this file. Change a view in Obsidian and Shelf leaves it alone from then on.
        filters:
          and:
            - file.inFolder("Movies")
            - 'managed_by == "mmdb"'
        properties:
          file.name:
            displayName: Film
          formats:
            displayName: Formats
          directors:
            displayName: Directors
          genres:
            displayName: Genres
          runtime:
            displayName: Runtime
          rating:
            displayName: Rating
          added:
            displayName: Added
        views:
          - type: cards
            name: Covers
            order:
              - file.name
              - formats
            sort:
              - property: file.name
                direction: ASC
            image: note.cover
            imageAspectRatio: 1.5
          - type: table
            name: All films
            order:
              - file.name
              - directors
              - formats
              - genres
              - runtime
              - rating
              - added
            sort:
              - property: file.name
                direction: ASC
          - type: table
            name: Recently added
            order:
              - file.name
              - formats
              - added
            sort:
              - property: added
                direction: DESC
            limit: 50

        """).ReplaceLineEndings("\n");

    public static readonly string ComicsBaseText = (BaseMarker + """
         · Shelf rewrites this file. Change a view in Obsidian and Shelf leaves it alone from then on.
        filters:
          and:
            - file.inFolder("Comics")
            - 'managed_by == "mmdb"'
        properties:
          file.name:
            displayName: Note
          series:
            displayName: Series
          volume:
            displayName: Volume
          issue:
            displayName: Issue
          variant:
            displayName: Variant
          publisher:
            displayName: Publisher
          cover_date:
            displayName: Cover date
          format:
            displayName: Format
          key_note:
            displayName: Key issue
          owned:
            displayName: Owned
          issues:
            displayName: Issues
          added:
            displayName: Added
        views:
          - type: cards
            name: Covers
            filters:
              and:
                - 'file.name != "_Series"'
            order:
              - file.name
              - publisher
            sort:
              - property: series
                direction: ASC
              - property: file.name
                direction: ASC
            image: note.cover
            imageAspectRatio: 1.5
          - type: table
            name: Issues
            filters:
              and:
                - 'format == "single issue"'
            order:
              - file.name
              - series
              - volume
              - issue
              - variant
              - publisher
              - cover_date
              - added
            sort:
              - property: series
                direction: ASC
              - property: file.name
                direction: ASC
          - type: table
            name: Series
            filters:
              and:
                - 'file.name == "_Series"'
            order:
              - series
              - volume
              - publisher
              - owned
              - issues
            sort:
              - property: series
                direction: ASC
          - type: table
            name: Key issues
            filters:
              and:
                - 'key_issue == true'
            order:
              - file.name
              - key_note
              - cover_date
            sort:
              - property: file.name
                direction: ASC
          - type: table
            name: Trades and hardcovers
            filters:
              and:
                - 'format != "single issue"'
                - 'file.name != "_Series"'
            order:
              - file.name
              - format
              - publisher
              - added
            sort:
              - property: file.name
                direction: ASC
          - type: table
            name: Recently added
            filters:
              and:
                - 'file.name != "_Series"'
            order:
              - file.name
              - format
              - added
            sort:
              - property: added
                direction: DESC
            limit: 50

        """).ReplaceLineEndings("\n");
}

/// <summary>A run of single issues as the index lists it.</summary>
/// <param name="SeriesNote">The series note's path in the vault, no extension.</param>
internal sealed record SeriesIndexEntry(string Title, string SeriesNote, IReadOnlyList<Comic> Copies, ComicSeries? Facts);
