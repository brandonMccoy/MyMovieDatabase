using System.Globalization;
using System.Text;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Vault;

/// <summary>The vault's shared files: the index, the guide for Claude, and the Bases views.</summary>
internal static class VaultFiles
{
    public const string Index = "_Index.md";
    public const string Guide = "CLAUDE.md";
    public const string MoviesBase = "Movies.base";

    /// <summary>Files Shelf may rewrite carry this; a version without it (the user's own) is left alone.</summary>
    public static bool IsManaged(string name, string text) => name switch
    {
        Guide => text.StartsWith(GuideMarker, StringComparison.Ordinal),
        MoviesBase => text.StartsWith(BaseMarker, StringComparison.Ordinal),
        _ => text.StartsWith("---\nmanaged_by: mmdb\n", StringComparison.Ordinal),
    };

    /// <param name="notes">Each film with its note's name (no folder or extension).</param>
    public static string RenderIndex(IReadOnlyList<(Movie Movie, string Note)> notes)
    {
        var index = new StringBuilder();
        index.Append("---\nmanaged_by: mmdb\n---\n");
        index.Append("# Collection index\n\n");
        index.Append("Shelf writes this file. One line per film: note · directors · formats owned.\n\n");

        var count = notes.Count;
        index.Append("**Movies:** ").Append(count.ToString("N0", CultureInfo.InvariantCulture)).Append(count == 1 ? " film" : " films");
        foreach (var format in Enum.GetValues<MediaFormat>())
        {
            var owned = notes.Count(n => n.Movie.Owns(format));
            if (owned > 0)
            {
                index.Append(" · ").Append(format.DisplayName()).Append(' ').Append(owned.ToString("N0", CultureInfo.InvariantCulture));
            }
        }

        index.Append("\n\n## Movies\n");
        foreach (var (movie, note) in notes.OrderBy(n => n.Note, StringComparer.InvariantCultureIgnoreCase).ThenBy(n => n.Note, StringComparer.Ordinal))
        {
            index.Append("- [[").Append(note).Append("]]");
            if (movie.Directors.Count > 0)
            {
                index.Append(" · ").AppendJoin(", ", movie.Directors.Select(Markdown.Escape));
            }

            index.Append(" · ").AppendJoin(", ", movie.Formats.Select(f => f.Format.DisplayName())).Append('\n');
        }

        return index.ToString();
    }

    private const string GuideMarker = "<!-- managed_by: mmdb";

    // Line endings normalized: a checkout may have turned this file's into CRLF.
    public static readonly string GuideText = (GuideMarker + """
         · Shelf rewrites this file. Delete this line to keep your own version. -->
        # Shelf collection vault

        This vault mirrors a physical movie collection kept in the Shelf app. Shelf writes these files and never reads them back, so changing them here doesn't change the collection.

        ## Answering questions
        - Start with `_Index.md`: the totals, then one line per film (note · directors · formats owned). "Do I own…", "how many 4K discs" or "which Carpenter films" can be answered from it alone.
        - Open a film's note in `Movies/` only for what the index doesn't have: cast, genres, runtime, rating, overview, barcodes, file paths, the user's own notes.
        - To filter many films on one field, search the frontmatter of `Movies/*.md` instead of opening the notes one by one.

        ## Layout
        - `Movies/<Title> (<Year>).md`: one note per film, however many formats it is owned on. Characters a file name can't hold are dropped or replaced (`:` becomes ` -`); a second film with the same name gets ` (2)`.
        - `Attachments/covers/`: cover images.
        - `Movies.base`: Obsidian Bases views of the films (covers, table, recently added).

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

        Keys without a value are left out. The body repeats the details with `[[links]]` to people, the barcode (UPC) of each disc, and a link to the file of a digital copy.

        ## Editing
        - Everything above `## My notes` is rewritten whenever the film changes in Shelf. Text under `## My notes` is kept.
        - To change the collection, use Shelf; new or renamed files in `Movies/` are not read.
        - When a film is removed from Shelf its note is deleted, or moved to `.trash/` if it had text under `## My notes`.
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
}
