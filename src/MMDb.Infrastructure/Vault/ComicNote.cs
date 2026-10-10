using System.Globalization;
using System.Text;
using MMDb.Domain.Comics;

namespace MMDb.Infrastructure.Vault;

/// <summary>
/// A comic's note (frontmatter with every field, a readable body, the user's "My notes") and a series' note
/// (<c>_Series.md</c>: the issues owned, with links, and the issues missing).
/// </summary>
internal static class ComicNote
{
    public const string SeriesNoteName = "_Series.md";

    /// <param name="cover">The cover's path in the vault, if it has one there.</param>
    /// <param name="series">The series note's path in the vault without <c>.md</c>, and the series' title; for single issues.</param>
    /// <param name="myNotes">What follows the My notes heading in the note being replaced.</param>
    public static string Render(Comic comic, string? cover, (string Note, string Title)? series, string myNotes, TimeZoneInfo timeZone)
    {
        var note = new StringBuilder();
        note.Append("---\n");
        MovieNote.Key(note, "mmdb_id", comic.Id.ToString("D"));
        MovieNote.Key(note, "managed_by", MovieNote.ManagedBy);
        MovieNote.Key(note, "series", Text(comic.Series));
        MovieNote.Key(note, "volume", comic.SeriesYear);
        MovieNote.Key(note, "issue", comic.Number is { } number ? Yaml.Quoted(number) : null);
        MovieNote.Key(note, "title", Text(comic.Title));
        MovieNote.Key(note, "variant", Text(comic.Variant));
        MovieNote.Key(note, "printing", comic.Printing);
        MovieNote.Key(note, "publisher", Text(comic.Publisher));
        MovieNote.Key(note, "cover_date", comic.CoverDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        MovieNote.Key(note, "format", FormatKey(comic.Format));
        MovieNote.List(note, "writers", comic.Writers);
        MovieNote.List(note, "artists", comic.Artists);
        if (comic.IsKeyIssue)
        {
            MovieNote.Key(note, "key_issue", "true");
            MovieNote.Key(note, "key_note", Text(comic.KeyNote));
        }

        MovieNote.Key(note, "upc", comic.Barcode is { } barcode ? Yaml.Quoted(barcode.Value) : null);
        MovieNote.Key(note, "isbn", comic.Isbn is { } isbn ? Yaml.Quoted(isbn.Value) : null);
        MovieNote.Key(note, "metron_id", comic.MetronId);
        MovieNote.Key(note, "cover", cover is null ? null : Yaml.Quoted($"[[{cover}]]"));
        MovieNote.Key(note, "added", TimeZoneInfo.ConvertTime(comic.CreatedAt, timeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        note.Append("---\n");

        note.Append("# ").Append(Markdown.Escape(comic.DisplayName)).Append('\n');
        if (cover is not null)
        {
            note.Append("![[").Append(cover).Append("|200]]\n");
        }

        note.Append('\n');
        var about = new List<string>();
        if (series is { } s)
        {
            about.Add($"[[{s.Note}|{Alias(s.Title)}]]");
        }
        else if (comic.Series is { } collectedFrom)
        {
            about.Add(Markdown.Escape(comic.Number is { } volume ? $"{collectedFrom} Vol. {volume}" : collectedFrom));
        }

        if (comic.Format.IsCollected())
        {
            about.Add(comic.Format.DisplayName());
        }

        if (comic.Publisher is { } publisher)
        {
            about.Add(Markdown.Escape(publisher));
        }

        if (comic.CoverDate is { } date)
        {
            about.Add(date.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
        }

        if (about.Count > 0)
        {
            note.AppendJoin(" · ", about).Append('\n');
        }

        if (!comic.Format.IsCollected() && comic.Title is { } story)
        {
            note.Append('*').Append(Markdown.Escape(story)).Append("*\n");
        }

        if (comic.IsKeyIssue)
        {
            note.Append("**KEY**");
            if (comic.KeyNote is { } keyNote)
            {
                note.Append(' ').Append(Markdown.Escape(keyNote));
            }

            note.Append('\n');
        }

        if (comic.Writers.Count > 0)
        {
            note.Append("**Written by** ").AppendJoin(", ", comic.Writers.Select(Markdown.PersonLink)).Append('\n');
        }

        if (comic.Artists.Count > 0)
        {
            note.Append("**Art by** ").AppendJoin(", ", comic.Artists.Select(Markdown.PersonLink)).Append('\n');
        }

        note.Append("**Copy** ").AppendJoin(" · ", Copy(comic)).Append('\n');
        if (!string.IsNullOrWhiteSpace(comic.Summary))
        {
            note.Append('\n').Append(Markdown.Escape(comic.Summary.Trim())).Append('\n');
        }

        note.Append('\n');
        note.Append("%% Shelf rewrites everything above \"My notes\" when the comic changes. Write your own notes below it. %%\n");
        note.Append(MovieNote.MyNotesHeading).Append('\n');
        note.Append(myNotes);
        return note.ToString();
    }

    /// <param name="folder">The series' folder in the vault, e.g. <c>Comics/Saga (2012)</c>.</param>
    /// <param name="issues">Each copy in the folder with its note's path in the vault (no <c>.md</c>).</param>
    /// <param name="facts">What Metron says about the series, if it is known.</param>
    public static string RenderSeries(string title, IReadOnlyList<(Comic Comic, string Note)> issues, ComicSeries? facts)
    {
        var first = issues[0].Comic;
        var publisher = issues.Select(i => i.Comic.Publisher).OfType<string>().FirstOrDefault() ?? facts?.Publisher;
        var byNumber = issues
            .GroupBy(i => i.Comic.Number!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, IssueNumber.Comparer)
            .ToList();
        var known = facts?.IssueNumbers ?? [];
        var missing = known.Where(n => !byNumber.Any(g => string.Equals(g.Key, n, StringComparison.OrdinalIgnoreCase))).ToList();
        var ownedCount = known.Count > 0 ? known.Count - missing.Count : byNumber.Count;

        var note = new StringBuilder();
        note.Append("---\nmanaged_by: mmdb\n");
        MovieNote.Key(note, "series", Text(first.Series));
        MovieNote.Key(note, "volume", first.SeriesYear);
        MovieNote.Key(note, "publisher", Text(publisher));
        MovieNote.Key(note, "owned", ownedCount);
        MovieNote.Key(note, "issues", known.Count > 0 ? known.Count : null);
        MovieNote.Key(note, "metron_series_id", facts?.MetronId);
        note.Append("---\n");

        note.Append("# ").Append(Markdown.Escape(title)).Append('\n');
        var about = new List<string>();
        if (publisher is not null)
        {
            about.Add(Markdown.Escape(publisher));
        }

        about.Add(known.Count > 0
            ? $"{Count(ownedCount)} of {Count(known.Count)} issues owned"
            : $"{Count(ownedCount)} {(ownedCount == 1 ? "issue" : "issues")} owned");
        note.AppendJoin(" · ", about).Append("\n\n");

        note.Append("## Owned\n");
        foreach (var number in byNumber)
        {
            note.Append("- ");
            var copies = number.OrderBy(i => i.Comic.Printing ?? 1).ThenBy(i => i.Comic.Variant ?? "", StringComparer.CurrentCultureIgnoreCase).ToList();
            if (copies.Count == 1)
            {
                note.Append("[[").Append(copies[0].Note).Append('|').Append(Alias("#" + number.Key)).Append("]]");
                if (copies[0].Comic.Variant is not null || copies[0].Comic.Printing is > 1)
                {
                    note.Append(" · ").Append(Markdown.Escape(CopyName(copies[0].Comic)));
                }
            }
            else
            {
                note.Append(Markdown.Escape("#" + number.Key)).Append(": ")
                    .AppendJoin(", ", copies.Select(c => $"[[{c.Note}|{Alias(CopyName(c.Comic))}]]"));
            }

            if (copies.Find(c => c.Comic.IsKeyIssue).Comic is { } key)
            {
                note.Append(" · **KEY**");
                if (key.KeyNote is { } keyNote)
                {
                    note.Append(' ').Append(Markdown.Escape(keyNote));
                }
            }

            note.Append('\n');
        }

        if (missing.Count > 0)
        {
            note.Append("\n## Missing\n").Append(Markdown.Escape(IssueRanges.Format(missing, known))).Append('\n');
        }

        return note.ToString();
    }

    /// <summary>How the copy reads in a list of the same issue: its variant and printing, "Regular cover" otherwise.</summary>
    private static string CopyName(Comic comic)
    {
        var parts = new List<string>();
        if (comic.Variant is { } variant)
        {
            parts.Add(variant);
        }

        if (comic.Printing is > 1 and var printing)
        {
            parts.Add($"{NoteNames.Ordinal(printing)} printing");
        }

        return parts.Count == 0 ? "Regular cover" : string.Join(", ", parts);
    }

    private static IEnumerable<string> Copy(Comic comic)
    {
        yield return comic.Format.DisplayName();
        if (comic.Variant is { } variant)
        {
            yield return Markdown.Escape(variant);
        }

        if (comic.Printing is { } printing)
        {
            yield return $"{NoteNames.Ordinal(printing)} printing";
        }

        if (comic.Barcode is { } barcode)
        {
            yield return $"UPC {barcode}";
        }

        if (comic.Isbn is { } isbn)
        {
            yield return $"ISBN {isbn.Value}";
        }
    }

    /// <summary>The frontmatter value of <c>format</c>: "single issue", "trade paperback", "hardcover", "omnibus".</summary>
    public static string FormatKey(ComicFormat format) => format.DisplayName().ToLowerInvariant();

    // A link's shown text can't hold the characters that end it.
    private static string Alias(string text) => text.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal).Replace("|", "-", StringComparison.Ordinal);

    private static string? Text(string? value) => value is null ? null : Yaml.Text(value);

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>Issue numbers written short: "#1–6, #8, #10–12".</summary>
internal static class IssueRanges
{
    /// <param name="numbers">The numbers to write.</param>
    /// <param name="order">Every issue in running order; numbers next to each other in it make a range.</param>
    public static string Format(IReadOnlyList<string> numbers, IReadOnlyList<string> order)
    {
        var position = order.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
        var sorted = numbers.OrderBy(n => position.GetValueOrDefault(n, int.MaxValue)).ThenBy(n => n, IssueNumber.Comparer).ToList();

        var parts = new List<string>();
        for (var start = 0; start < sorted.Count;)
        {
            var end = start;
            while (end + 1 < sorted.Count
                && position.TryGetValue(sorted[end], out var here)
                && position.TryGetValue(sorted[end + 1], out var next)
                && next == here + 1)
            {
                end++;
            }

            parts.Add(end == start ? $"#{sorted[start]}" : $"#{sorted[start]}–{sorted[end]}");
            start = end + 1;
        }

        return string.Join(", ", parts);
    }
}
