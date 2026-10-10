using System.Globalization;
using MMDb.Contracts.Comics;
using MMDb.UI.Components;

namespace MMDb.UI.Comics;

/// <summary>What the add/edit comic screen is editing, in the shape the inputs bind to.</summary>
public sealed class ComicForm
{
    public ComicFormatKind Format { get; set; } = ComicFormatKind.SingleIssue;

    public string Series { get; set; } = "";

    public int? SeriesYear { get; set; }

    public string Number { get; set; } = "";

    public string Title { get; set; } = "";

    public string Variant { get; set; } = "";

    public int? Printing { get; set; }

    public string Publisher { get; set; } = "";

    /// <summary>"yyyy-MM", as a month input gives it.</summary>
    public string CoverMonth { get; set; } = "";

    /// <summary>Comma-separated, as typed.</summary>
    public string Writers { get; set; } = "";

    /// <summary>Comma-separated, as typed.</summary>
    public string Artists { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Barcode { get; set; } = "";

    public string Isbn { get; set; } = "";

    public bool IsKeyIssue { get; set; }

    public string KeyNote { get; set; } = "";

    // Not edited by hand: they come from a lookup and must survive an edit.
    public int? MetronId { get; set; }

    public int? MetronSeriesId { get; set; }

    public string? CoverSource { get; set; }

    public bool IsCollected => ComicText.IsCollected(Format);

    /// <summary>The minimum to save: a series and number for an issue, a title or series for a trade. The PC checks the rest.</summary>
    public bool CanSave => IsCollected
        ? Title.Trim().Length > 0 || Series.Trim().Length > 0
        : Series.Trim().Length > 0 && Number.Trim().TrimStart('#').Length > 0;

    public IReadOnlyList<string> WriterList => Split(Writers);

    public IReadOnlyList<string> ArtistList => Split(Artists);

    public DateOnly? CoverDate => DateOnly.TryParseExact(CoverMonth, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? month : null;

    /// <summary>"Saga #7" or the trade's title, for the placeholder cover.</summary>
    public string PosterTitle => (IsCollected ? Blank(Title) ?? Blank(Series) : Blank(Series)) ?? "Untitled";

    public static ComicForm From(ComicDto comic) => From(new SaveComicRequest
    {
        Format = comic.Format,
        Series = comic.Series,
        SeriesYear = comic.SeriesYear,
        Number = comic.Number,
        Title = comic.Title,
        Variant = comic.Variant,
        Printing = comic.Printing,
        Publisher = comic.Publisher,
        CoverDate = comic.CoverDate,
        Writers = comic.Writers,
        Artists = comic.Artists,
        Summary = comic.Summary,
        Barcode = comic.Barcode,
        Isbn = comic.Isbn,
        MetronId = comic.MetronId,
        MetronSeriesId = comic.MetronSeriesId,
        IsKeyIssue = comic.IsKeyIssue,
        KeyNote = comic.KeyNote,
        CoverSource = comic.CoverSource,
    });

    public static ComicForm From(SaveComicRequest details) => new()
    {
        Format = details.Format,
        Series = details.Series ?? "",
        SeriesYear = details.SeriesYear,
        Number = details.Number ?? "",
        Title = details.Title ?? "",
        Variant = details.Variant ?? "",
        Printing = details.Printing,
        Publisher = details.Publisher ?? "",
        CoverMonth = details.CoverDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "",
        Writers = string.Join(", ", details.Writers),
        Artists = string.Join(", ", details.Artists),
        Summary = details.Summary ?? "",
        Barcode = details.Barcode ?? "",
        Isbn = details.Isbn ?? "",
        IsKeyIssue = details.IsKeyIssue,
        KeyNote = details.KeyNote ?? "",
        MetronId = details.MetronId,
        MetronSeriesId = details.MetronSeriesId,
        CoverSource = details.CoverSource,
    };

    /// <summary>Takes what a lookup found and keeps what makes this copy: its variant, printing, barcode, ISBN and key note.</summary>
    public void Apply(SaveComicRequest found)
    {
        var mine = (Variant, Printing, Barcode, Isbn, IsKeyIssue, KeyNote, Format);
        var from = From(found);
        Series = from.Series;
        SeriesYear = from.SeriesYear;
        Number = from.Number;
        Title = from.Title;
        Publisher = from.Publisher;
        CoverMonth = from.CoverMonth;
        Writers = from.Writers;
        Artists = from.Artists;
        Summary = from.Summary;
        MetronId = from.MetronId;
        MetronSeriesId = from.MetronSeriesId;
        CoverSource = from.CoverSource;
        (Variant, Printing, Barcode, Isbn, IsKeyIssue, KeyNote, Format) = mine;
        if (Barcode.Length == 0)
        {
            Barcode = from.Barcode;
        }
    }

    public SaveComicRequest ToRequest() => new()
    {
        Format = Format,
        Series = Blank(Series),
        SeriesYear = SeriesYear,
        Number = Blank(Number),
        Title = Blank(Title),
        Variant = Blank(Variant),
        Printing = Printing,
        Publisher = Blank(Publisher),
        CoverDate = CoverDate,
        Writers = WriterList,
        Artists = ArtistList,
        Summary = Blank(Summary),
        Barcode = IsCollected ? null : Blank(Barcode),
        Isbn = IsCollected ? Blank(Isbn) : null,
        MetronId = MetronId,
        MetronSeriesId = MetronSeriesId,
        IsKeyIssue = IsKeyIssue,
        KeyNote = IsKeyIssue ? Blank(KeyNote) : null,
        CoverSource = CoverSource,
    };

    private static string[] Split(string names) => names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What was searched for and found, kept while the user reviews a match.</summary>
public sealed class ComicSearchState
{
    public string Query { get; set; } = "";

    /// <summary>The query <see cref="Response"/> answers.</summary>
    public string? SearchedFor { get; set; }

    public ComicLookupResponse? Response { get; set; }
}
