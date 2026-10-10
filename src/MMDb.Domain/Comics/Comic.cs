namespace MMDb.Domain.Comics;

/// <summary>One comic copy in the collection: a single issue (one row per variant owned) or a collected edition.</summary>
public sealed class Comic : ProductBase
{
    // Stored as two columns, so every issue of a series can be found by its UPC.
    private string? _upc;
    private string? _upcSupplement;

    // For EF Core.
    private Comic()
    {
    }

    public ComicFormat Format { get; private set; }

    public string? Series { get; private set; }

    public int? SeriesYear { get; private set; }

    public string? Number { get; private set; }

    public string? Title { get; private set; }

    public string? Variant { get; private set; }

    public int? Printing { get; private set; }

    public string? Publisher { get; private set; }

    public DateOnly? CoverDate { get; private set; }

    public IReadOnlyList<string> Writers { get; private set; } = [];

    public IReadOnlyList<string> Artists { get; private set; } = [];

    public string? Summary { get; private set; }

    public ComicBarcode? Barcode
    {
        get => _upc is null ? null : ComicBarcode.Parse(_upc + _upcSupplement);
        private set
        {
            _upc = value?.Upc.Value;
            _upcSupplement = value?.Supplement;
        }
    }

    public Isbn? Isbn { get; private set; }

    public int? MetronId { get; private set; }

    public int? MetronSeriesId { get; private set; }

    public bool IsKeyIssue { get; private set; }

    public string? KeyNote { get; private set; }

    public string? CoverUrl { get; private set; }

    public ComicDetails Details => new()
    {
        Format = Format,
        Series = Series,
        SeriesYear = SeriesYear,
        Number = Number,
        Title = Title,
        Variant = Variant,
        Printing = Printing,
        Publisher = Publisher,
        CoverDate = CoverDate,
        Writers = Writers,
        Artists = Artists,
        Summary = Summary,
        Barcode = Barcode?.Value,
        Isbn = Isbn?.Value,
        MetronId = MetronId,
        MetronSeriesId = MetronSeriesId,
        IsKeyIssue = IsKeyIssue,
        KeyNote = KeyNote,
        CoverUrl = CoverUrl,
    };

    /// <summary>"Daredevil #181", "Saga Vol. 1" or a collected edition's title.</summary>
    public string DisplayName => Details.DisplayName;

    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="ComicDetails.Validate"/>).</exception>
    public static Comic Create(ComicDetails details, DateTimeOffset now)
    {
        var comic = new Comic { Id = Guid.CreateVersion7(now), CreatedAt = now };
        comic.Update(details, now);
        return comic;
    }

    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="ComicDetails.Validate"/>).</exception>
    public void Update(ComicDetails details, DateTimeOffset now)
    {
        var errors = details.Validate();
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var d = details.Normalized();
        Format = d.Format;
        Series = d.Series;
        SeriesYear = d.SeriesYear;
        Number = d.Number;
        Title = d.Title;
        Variant = d.Variant;
        Printing = d.Printing;
        Publisher = d.Publisher;
        CoverDate = d.CoverDate;
        Writers = d.Writers;
        Artists = d.Artists;
        Summary = d.Summary;
        Barcode = d.ParsedBarcode;
        Isbn = d.ParsedIsbn;
        MetronId = d.MetronId;
        MetronSeriesId = d.MetronSeriesId;
        IsKeyIssue = d.IsKeyIssue;
        KeyNote = d.KeyNote;
        CoverUrl = d.CoverUrl;
        Touch(now);
    }

    /// <summary>
    /// Whether <paramref name="details"/> describe this very copy rather than another variant or printing: the same
    /// barcode with its add-on, the same ISBN, or the same Metron issue with the same variant and printing.
    /// </summary>
    public bool IsSameCopyAs(ComicDetails details)
    {
        var d = details.Normalized();
        if (Barcode?.Supplement is not null && d.ParsedBarcode is { } barcode)
        {
            return Barcode == barcode;
        }

        if (Isbn is not null && d.ParsedIsbn is { } isbn)
        {
            return Isbn == isbn;
        }

        return MetronId is not null && MetronId == d.MetronId
            && string.Equals(Variant, d.Variant, StringComparison.OrdinalIgnoreCase)
            && (Printing ?? 1) == (d.Printing ?? 1);
    }
}
