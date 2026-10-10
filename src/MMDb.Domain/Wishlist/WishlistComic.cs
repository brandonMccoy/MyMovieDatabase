using MMDb.Domain.Comics;

namespace MMDb.Domain.Wishlist;

/// <summary>
/// A comic to buy: one issue or one collected edition. Which cover or printing doesn't matter: any copy of the
/// issue grants the wish.
/// </summary>
public sealed class WishlistComic : ProductBase
{
    // For EF Core.
    private WishlistComic()
    {
    }

    public ComicFormat Format { get; private set; }

    public string? Series { get; private set; }

    public int? SeriesYear { get; private set; }

    /// <summary>The issue number; for a collected edition, its volume number, if it has one.</summary>
    public string? Number { get; private set; }

    /// <summary>A collected edition's title.</summary>
    public string? Title { get; private set; }

    public string? Publisher { get; private set; }

    public string? Isbn { get; private set; }

    /// <summary>The Metron issue, for an issue found there.</summary>
    public int? MetronId { get; private set; }

    public int? MetronSeriesId { get; private set; }

    /// <summary>Where the cover image can be downloaded.</summary>
    public string? CoverUrl { get; private set; }

    /// <summary>Added from a series' missing issues in the library.</summary>
    public bool FromMissing { get; private set; }

    /// <summary>"Saga #7", "Saga Vol. 1" or a collected edition's title.</summary>
    public string DisplayName => new ComicDetails { Format = Format, Series = Series, SeriesYear = SeriesYear, Number = Number, Title = Title }.DisplayName;

    /// <summary>Validation errors for this input; empty when <see cref="Create"/> would accept it.</summary>
    public static IReadOnlyList<ValidationError> Validate(WantedComic wanted) => wanted.AsDetails().Validate();

    /// <exception cref="DomainValidationException">The input is invalid (see <see cref="Validate"/>).</exception>
    public static WishlistComic Create(WantedComic wanted, DateTimeOffset now)
    {
        var errors = Validate(wanted);
        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var d = wanted.AsDetails().Normalized();
        var item = new WishlistComic
        {
            Id = Guid.CreateVersion7(now),
            CreatedAt = now,
            Format = d.Format,
            Series = d.Series,
            SeriesYear = d.SeriesYear,
            Number = d.Number,
            Title = d.Title,
            Publisher = d.Publisher,
            Isbn = d.Isbn,
            MetronId = d.MetronId,
            MetronSeriesId = d.MetronSeriesId,
            CoverUrl = d.CoverUrl,
            FromMissing = wanted.FromMissing,
        };
        item.Touch(now);
        return item;
    }

    /// <summary>Metron's entry for the issue became known after it was wanted: its id, and where its cover is.</summary>
    public void Found(int metronId, string? coverUrl, DateTimeOffset now)
    {
        MetronId = metronId;
        CoverUrl = coverUrl ?? CoverUrl;
        Touch(now);
    }

    /// <summary>Whether a copy of <paramref name="comic"/> is the comic wanted.</summary>
    public bool IsFor(ComicDetails comic)
    {
        var c = comic.Normalized();
        if (Format.IsCollected() != c.Format.IsCollected())
        {
            return false;
        }

        if (!Format.IsCollected())
        {
            return IsIssue(c.MetronSeriesId, c.Series, c.SeriesYear, c.Number);
        }

        if (Isbn is not null && c.Isbn is not null)
        {
            return Isbn == c.Isbn;
        }

        return Title is not null && c.Title is not null
            ? string.Equals(Title, c.Title, StringComparison.OrdinalIgnoreCase)
            : Series is not null && IsSameSeries(c.MetronSeriesId, c.Series, c.SeriesYear) && string.Equals(Number, c.Number, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this is that issue of that series: the same series on Metron, or the same name and starting year,
    /// and the same number.
    /// </summary>
    public bool IsIssue(int? metronSeriesId, string? series, int? seriesYear, string? number) =>
        !Format.IsCollected()
        && number is not null
        && string.Equals(Number, number, StringComparison.OrdinalIgnoreCase)
        && IsSameSeries(metronSeriesId, series, seriesYear);

    private bool IsSameSeries(int? metronSeriesId, string? series, int? seriesYear) =>
        MetronSeriesId is { } mine && metronSeriesId is { } theirs
            ? mine == theirs
            : string.Equals(Series, series?.Trim(), StringComparison.OrdinalIgnoreCase) && (SeriesYear is null || seriesYear is null || SeriesYear == seriesYear);
}

/// <summary>User input for a comic on the wishlist.</summary>
public sealed record WantedComic
{
    public ComicFormat Format { get; init; } = ComicFormat.SingleIssue;

    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    /// <summary>The issue number; a collected edition's volume number.</summary>
    public string? Number { get; init; }

    public string? Title { get; init; }

    public string? Publisher { get; init; }

    public string? Isbn { get; init; }

    public int? MetronId { get; init; }

    public int? MetronSeriesId { get; init; }

    public string? CoverUrl { get; init; }

    public bool FromMissing { get; init; }

    /// <summary>The comic wanted, as a copy of it would be described.</summary>
    public ComicDetails AsDetails() => new()
    {
        Format = Format,
        Series = Series,
        SeriesYear = SeriesYear,
        Number = Number,
        Title = Title,
        Publisher = Publisher,
        Isbn = Isbn,
        MetronId = MetronId,
        MetronSeriesId = MetronSeriesId,
        CoverUrl = CoverUrl,
    };
}
