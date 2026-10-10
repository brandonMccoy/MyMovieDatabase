namespace MMDb.Domain.Comics;

/// <summary>
/// The user-editable fields of one comic copy: a single issue (series + number) or a collected edition
/// (a trade, hardcover or omnibus: a title, or a series and volume number). Each variant owned is its own copy.
/// </summary>
public sealed record ComicDetails
{
    public const int MinYear = 1890;
    public const int MaxYear = 2100;
    public const int MaxTextLength = 300;
    public const int MaxNumberLength = 16;
    public const int MaxShortTextLength = 100;
    public const int MaxPrinting = 99;
    public const int MaxCoverUrlLength = 500;

    public ComicFormat Format { get; init; } = ComicFormat.SingleIssue;

    public string? Series { get; init; }

    /// <summary>The year the series began, which tells its volumes apart: Daredevil (1964), Daredevil (1998).</summary>
    public int? SeriesYear { get; init; }

    /// <summary>The issue number ("181", "½", "1.MU"); for a collected edition, its volume number in the series.</summary>
    public string? Number { get; init; }

    /// <summary>A collected edition's title ("Saga, Volume One"), or an issue's story title ("Death of Elektra").</summary>
    public string? Title { get; init; }

    /// <summary>Which cover: "Cover A", "Cover B", "1:25 Ross variant".</summary>
    public string? Variant { get; init; }

    public int? Printing { get; init; }

    public string? Publisher { get; init; }

    /// <summary>The date on the cover; only its month and year mean anything.</summary>
    public DateOnly? CoverDate { get; init; }

    public IReadOnlyList<string> Writers { get; init; } = [];

    public IReadOnlyList<string> Artists { get; init; } = [];

    public string? Summary { get; init; }

    /// <summary>The cover's barcode with its add-on, as typed or scanned (see <see cref="ComicBarcode"/>).</summary>
    public string? Barcode { get; init; }

    /// <summary>A collected edition's ISBN, as typed or scanned.</summary>
    public string? Isbn { get; init; }

    public int? MetronId { get; init; }

    public int? MetronSeriesId { get; init; }

    public bool IsKeyIssue { get; init; }

    /// <summary>Why it's a key issue: "1st Elektra". Dropped unless <see cref="IsKeyIssue"/>.</summary>
    public string? KeyNote { get; init; }

    /// <summary>Where the cover image can be downloaded (Metron or Open Library).</summary>
    public string? CoverUrl { get; init; }

    internal ComicBarcode? ParsedBarcode => ComicBarcode.TryParse(Barcode, out var barcode) ? barcode : null;

    internal Domain.Isbn? ParsedIsbn => Domain.Isbn.TryParse(Isbn, out var isbn) ? isbn : null;

    /// <summary>"Daredevil #181", "Saga Vol. 1" or a collected edition's title.</summary>
    public string DisplayName => Normalized() is var d && d.Format.IsCollected()
        ? d.Title ?? (d.Series is null ? "" : d.Number is null ? d.Series : $"{d.Series} Vol. {d.Number}")
        : $"{d.Series} #{d.Number}".Trim();

    /// <summary>Trims text, turns blanks into nulls, drops empty and duplicate names, and tidies codes and numbers.</summary>
    public ComicDetails Normalized() => this with
    {
        Series = NullIfBlank(Series),
        Number = IssueNumber.Normalize(Number),
        Title = NullIfBlank(Title),
        Variant = NullIfBlank(Variant),
        Publisher = NullIfBlank(Publisher),
        Writers = DistinctNonBlank(Writers),
        Artists = DistinctNonBlank(Artists),
        Summary = NullIfBlank(Summary),
        Barcode = ParsedBarcode?.Value ?? NullIfBlank(Barcode),
        Isbn = ParsedIsbn?.Value ?? NullIfBlank(Isbn),
        KeyNote = IsKeyIssue ? NullIfBlank(KeyNote) : null,
        CoverUrl = NullIfBlank(CoverUrl),
    };

    /// <summary>Validates the normalized form of these details.</summary>
    public IReadOnlyList<ValidationError> Validate()
    {
        var d = Normalized();
        var errors = new List<ValidationError>();

        if (!Enum.IsDefined(d.Format))
        {
            errors.Add(new(ComicFields.Format, $"Unknown format {d.Format}."));
            return errors;
        }

        if (d.Format.IsCollected())
        {
            if (d.Title is null && d.Series is null)
            {
                errors.Add(new(ComicFields.Title, "A title or a series is required."));
            }
        }
        else
        {
            if (d.Series is null)
            {
                errors.Add(new(ComicFields.Series, "Series is required."));
            }

            if (d.Number is null)
            {
                errors.Add(new(ComicFields.Number, "Issue number is required."));
            }
        }

        TooLong(errors, ComicFields.Series, "Series", d.Series, MaxTextLength);
        TooLong(errors, ComicFields.Title, "Title", d.Title, MaxTextLength);
        TooLong(errors, ComicFields.Number, "Number", d.Number, MaxNumberLength);
        TooLong(errors, ComicFields.Variant, "Variant", d.Variant, MaxShortTextLength);
        TooLong(errors, ComicFields.Publisher, "Publisher", d.Publisher, MaxShortTextLength);
        TooLong(errors, ComicFields.KeyNote, "Key issue note", d.KeyNote, MaxShortTextLength);

        if (d.SeriesYear is < MinYear or > MaxYear)
        {
            errors.Add(new(ComicFields.SeriesYear, $"Year must be between {MinYear} and {MaxYear}."));
        }

        if (d.Printing is < 1 or > MaxPrinting)
        {
            errors.Add(new(ComicFields.Printing, $"Printing must be between 1 and {MaxPrinting}."));
        }

        if (d.Writers.Any(w => w.Length > MaxShortTextLength))
        {
            errors.Add(new(ComicFields.Writers, $"A name can be at most {MaxShortTextLength} characters."));
        }

        if (d.Artists.Any(a => a.Length > MaxShortTextLength))
        {
            errors.Add(new(ComicFields.Artists, $"A name can be at most {MaxShortTextLength} characters."));
        }

        if (d.Barcode is not null && d.ParsedBarcode is null)
        {
            errors.Add(new(ComicFields.Barcode, "Not a valid barcode: the UPC (12 or 13 digits), then the 5-digit add-on if there is one."));
        }

        if (d.Isbn is not null && d.ParsedIsbn is null)
        {
            errors.Add(new(ComicFields.Isbn, "Not a valid ISBN (13 digits starting 978 or 979, or the older 10)."));
        }

        // It is downloaded and named after, so only plain https links are accepted.
        if (d.CoverUrl is { } url
            && (url.Length > MaxCoverUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new(ComicFields.CoverUrl, "Not an https link to an image."));
        }

        return errors;
    }

    private static void TooLong(List<ValidationError> errors, string field, string label, string? value, int max)
    {
        if (value?.Length > max)
        {
            errors.Add(new(field, $"{label} can be at most {max} characters."));
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] DistinctNonBlank(IEnumerable<string> values) =>
        [.. values
            .Select(v => v?.Trim())
            .OfType<string>()
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
