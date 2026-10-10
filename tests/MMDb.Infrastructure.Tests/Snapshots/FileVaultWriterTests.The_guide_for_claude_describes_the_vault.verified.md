<!-- managed_by: mmdb · Shelf rewrites this file. Delete this line to keep your own version. -->
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
