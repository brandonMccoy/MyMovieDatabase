<!-- managed_by: mmdb · Shelf rewrites this file. Delete this line to keep your own version. -->
# Shelf collection vault

This vault mirrors a collection of movies (on disc and as digital files) and comics kept in the Shelf app. Shelf writes these files and never reads them back, so changing them here doesn't change the collection.

## Answering questions
- Start with `_Index.md`: the totals, then one line per film (note · directors · formats owned), one line per comic series (publisher · issues owned · missing · key issues) and one per trade or hardcover. "Do I own…", "how many 4K discs", "which Daredevil issues am I missing" or "which Carpenter films" can be answered from it alone.
- Open a film's note in `Movies/` only for what the index doesn't have: cast, crew, genres, runtime, rating, overview, barcodes, file paths, the user's own notes.
- Everyone in a film's cast and crew is a `[[link]]` in its note; people have no notes of their own unless the user made one, so search the notes for `[[A Name]]` to find every film someone worked on. Writers, producers, cinematographers, composers and editors have a line each under the directors and are in the frontmatter; the rest of the crew is under `## Crew`, one line per job title.
- A series' `_Series.md` lists every issue owned (each copy linked) and every issue missing. Open an issue's note for its creators, cover date, variant, barcode, summary and the user's notes.
- What the user wants but doesn't own is under `## Wishlist` at the end of the index: a film with the format wanted (and the formats owned, when it is an upgrade), a comic issue or a book. "Is X on my wishlist" and "what should I look for in a shop" are answered there.
- To filter many notes on one field, search the frontmatter of `Movies/*.md` or `Comics/**/*.md` instead of opening the notes one by one.

## Layout
- `Movies/<Title> (<Year>).md`: one note per film, however many formats it is owned on. Characters a file name can't hold are dropped or replaced (`:` becomes ` -`); a second film with the same name gets ` (2)`.
- `Comics/<Series> (<Year it began>)/<Series> <Issue>.md`: one note per comic copy, so a variant cover owned as well has its own note (`Saga 7 (Cover B).md`). File names leave out `#`, which can't be in a link; headings keep it.
- `Comics/<Series> (<Year it began>)/_Series.md`: the series, its issues owned and missing.
- `Comics/Trades & HCs/<Title>.md`: trades, hardcovers and omnibuses.
- `Attachments/covers/`: cover images.
- `Wishlist/<Title> (<Year>) (wanted).md`, `Wishlist/<Series> <Issue> (wanted).md`: one note per film or comic the user wants and does not own yet. They are in the index under `## Wishlist`, never among the films or comics owned.
- `Movies.base`, `Comics.base`, `Wishlist.base`: Obsidian Bases views (covers, tables, recently added, key issues).

## Movie note frontmatter
| Key | Type | Meaning |
|---|---|---|
| `mmdb_id` | text | Shelf's id for the film; it never changes |
| `managed_by` | text | always `mmdb`: Shelf writes this note |
| `title` | text | |
| `year` | number | release year |
| `formats` | list | formats owned: `4K UHD`, `Blu-ray`, `DVD`, `Digital` |
| `directors` | list | |
| `writers` | list | screenplay, story and the novel it is based on |
| `producers` | list | |
| `executive_producers` | list | |
| `cinematographers` | list | directors of photography |
| `composers` | list | original music |
| `editors` | list | |
| `cast` | list | the ten top-billed actors, in billing order; the note links the whole cast |
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

## Wishlist note frontmatter
| Key | Type | Meaning |
|---|---|---|
| `mmdb_id` | text | Shelf's id for the wishlist item |
| `managed_by` | text | always `mmdb` |
| `wishlist` | text | `movie` or `comic`: the note is something wanted, not owned |
| `title`, `year`, `directors`, `tmdb_id` | | a film, as in a movie note |
| `wanted_format` | text | the one format the film is wanted on |
| `owned_formats` | list | formats the film is owned on already (the wish is an upgrade) |
| `series`, `volume`, `issue`, `publisher`, `format`, `isbn` | | a comic, as in a comic note |
| `from_missing` | boolean | `true` if it was added from a series' missing issues |
| `cover` | link | the cover image |
| `added` | date | when it was put on the wishlist |

`_Series.md` frontmatter: `series`, `volume`, `publisher`, `owned` (issue numbers owned; variants count once), `issues` (issues in the series according to Metron, if known), `metron_series_id`.

Keys without a value are left out. Note bodies repeat the details with `[[links]]` to people (and from an issue to its series), the barcode of each disc or comic, and a link to the file of a digital copy.

## Editing
- Everything above `## My notes` is rewritten whenever the film or comic changes in Shelf. Text under `## My notes` is kept. `_Series.md` has no My notes: Shelf rewrites all of it.
- To change the collection, use Shelf; new or renamed files in `Movies/` and `Comics/` are not read.
- When a film or comic is removed from Shelf its note is deleted, or moved to `.trash/` if it had text under `## My notes`. The same happens to a wishlist note once the film or comic is bought: it then has a note in `Movies/` or `Comics/`.
- Files without `managed_by: mmdb` are never touched.
