using System.Globalization;
using System.Text;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Infrastructure.Vault;

/// <summary>
/// The markdown note of something on the wishlist, in <c>Wishlist/</c>. Like a film's or comic's note it carries
/// Shelf's id and keeps what is written under "My notes"; <c>wishlist: movie</c> or <c>wishlist: comic</c> tells
/// it apart from a note of something owned.
/// </summary>
internal static class WishNote
{
    /// <param name="owned">The formats the film is owned on already.</param>
    public static string Render(WishlistMovie film, IReadOnlyList<MediaFormat> owned, string? cover, string myNotes, TimeZoneInfo timeZone)
    {
        var note = new StringBuilder();
        note.Append("---\n");
        MovieNote.Key(note, "mmdb_id", film.Id.ToString("D"));
        MovieNote.Key(note, "managed_by", MovieNote.ManagedBy);
        MovieNote.Key(note, "wishlist", "movie");
        MovieNote.Key(note, "title", Yaml.Text(film.Title));
        MovieNote.Key(note, "year", film.Year);
        MovieNote.Key(note, "wanted_format", film.Format.DisplayName());
        MovieNote.List(note, "owned_formats", owned.Select(f => f.DisplayName()));
        MovieNote.List(note, "directors", film.Directors);
        MovieNote.Key(note, "tmdb_id", film.TmdbId);
        MovieNote.Key(note, "cover", cover is null ? null : Yaml.Quoted($"[[{cover}]]"));
        MovieNote.Key(note, "added", Day(film.CreatedAt, timeZone));
        note.Append("---\n");

        note.Append("# ").Append(Markdown.Escape(film.Title));
        if (film.Year is { } year)
        {
            note.Append(" (").Append(year.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        note.Append('\n');
        Cover(note, cover);
        note.Append("**Wanted on** ").Append(film.Format.DisplayName());
        if (owned.Count > 0)
        {
            note.Append(" · an upgrade: owned on ").AppendJoin(", ", owned.Select(f => f.DisplayName()));
        }

        note.Append('\n');
        if (film.Directors.Count > 0)
        {
            note.Append("**Directed by** ").AppendJoin(", ", film.Directors.Select(Markdown.PersonLink)).Append('\n');
        }

        note.Append("[Trailer](").Append(Trailer.UrlFor(film.Title, film.Year, film.TrailerYouTubeKey).AbsoluteUri).Append(')');
        if (film.TmdbId is { } tmdbId)
        {
            note.Append(" · [TMDB](https://www.themoviedb.org/movie/").Append(tmdbId.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        note.Append("\n\n");
        MyNotes(note, myNotes);
        return note.ToString();
    }

    public static string Render(WishlistComic comic, string? cover, string myNotes, TimeZoneInfo timeZone)
    {
        var note = new StringBuilder();
        note.Append("---\n");
        MovieNote.Key(note, "mmdb_id", comic.Id.ToString("D"));
        MovieNote.Key(note, "managed_by", MovieNote.ManagedBy);
        MovieNote.Key(note, "wishlist", "comic");
        MovieNote.Key(note, "series", comic.Series is { } series ? Yaml.Text(series) : null);
        MovieNote.Key(note, "volume", comic.SeriesYear);
        MovieNote.Key(note, "issue", comic.Number is { } number ? Yaml.Quoted(number) : null);
        MovieNote.Key(note, "title", comic.Title is { } title ? Yaml.Text(title) : null);
        MovieNote.Key(note, "publisher", comic.Publisher is { } publisher ? Yaml.Text(publisher) : null);
        MovieNote.Key(note, "format", ComicNote.FormatKey(comic.Format));
        MovieNote.Key(note, "isbn", comic.Isbn is { } isbn ? Yaml.Quoted(isbn) : null);
        MovieNote.Key(note, "from_missing", comic.FromMissing ? "true" : null);
        MovieNote.Key(note, "cover", cover is null ? null : Yaml.Quoted($"[[{cover}]]"));
        MovieNote.Key(note, "added", Day(comic.CreatedAt, timeZone));
        note.Append("---\n");

        note.Append("# ").Append(Markdown.Escape(comic.DisplayName)).Append('\n');
        Cover(note, cover);
        note.Append("**Wanted** ").Append(About(comic)).Append('\n');
        if (comic.Isbn is { } code)
        {
            note.Append("**ISBN** ").Append(code).Append('\n');
        }

        note.Append('\n');
        MyNotes(note, myNotes);
        return note.ToString();
    }

    /// <summary>"Image · 2012 series · missing from the run", "Image Comics · trade paperback".</summary>
    public static string About(WishlistComic comic)
    {
        var parts = new List<string>();
        if (comic.Publisher is { } publisher)
        {
            parts.Add(Markdown.Escape(publisher));
        }

        if (comic.Format.IsCollected())
        {
            parts.Add(ComicNote.FormatKey(comic.Format));
        }
        else
        {
            if (comic.SeriesYear is { } year)
            {
                parts.Add(year.ToString(CultureInfo.InvariantCulture) + " series");
            }

            if (comic.FromMissing)
            {
                parts.Add("missing from the run");
            }
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : ComicNote.FormatKey(comic.Format);
    }

    private static void Cover(StringBuilder note, string? cover)
    {
        if (cover is not null)
        {
            note.Append("![[").Append(cover).Append("|200]]\n");
        }

        note.Append('\n');
    }

    private static void MyNotes(StringBuilder note, string myNotes)
    {
        note.Append("%% Shelf rewrites everything above \"My notes\" when the wish changes, and removes the note once it is bought. Write your own notes below it. %%\n");
        note.Append(MovieNote.MyNotesHeading).Append('\n');
        note.Append(myNotes);
    }

    private static string Day(DateTimeOffset at, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(at, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
