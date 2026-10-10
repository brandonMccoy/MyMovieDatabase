using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Application.Metadata;

/// <summary>Port: an online comics database that knows single issues and their series (Metron).</summary>
public interface IComicMetadata
{
    /// <summary>Ids of the issues matching the search, in the database's order.</summary>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<IReadOnlyList<int>> SearchAsync(IssueSearch search, CancellationToken cancellationToken = default);

    /// <summary>One issue's details, ready to edit (Metron ids and cover link included), and its variant covers.</summary>
    /// <returns><see langword="null"/> if the database has no such issue.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<IssueFacts?> GetIssueAsync(int metronId, CancellationToken cancellationToken = default);

    /// <summary>A series and every issue number in it.</summary>
    /// <returns><see langword="null"/> if the database has no such series.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<SeriesFacts?> GetSeriesAsync(int metronSeriesId, CancellationToken cancellationToken = default);

    /// <summary>Every issue of a series as its list shows it: enough to name an issue and show its cover.</summary>
    /// <returns>Empty if the database has no such series.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<IReadOnlyList<IssueListing>> ListIssuesAsync(int metronSeriesId, CancellationToken cancellationToken = default);
}

/// <param name="Number">As the database writes it; compare after <see cref="IssueNumber.Normalize"/>.</param>
/// <param name="CoverUrl">Where the regular cover can be downloaded, if the database has one.</param>
public sealed record IssueListing(int MetronId, string Number, string? CoverUrl);

/// <summary>Port: an online book database, for collected editions found by ISBN (Open Library).</summary>
public interface IBookMetadata
{
    /// <returns>The book's details as a trade paperback or hardcover; <see langword="null"/> if it is unknown.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<ComicDetails?> FindByIsbnAsync(Isbn isbn, CancellationToken cancellationToken = default);
}

/// <summary>Port: downloads cover images from the comic databases.</summary>
public interface ICoverSource
{
    /// <param name="url">See <see cref="ComicDetails.CoverUrl"/>.</param>
    /// <returns>The image file's bytes, or <see langword="null"/> if it could not be downloaded.</returns>
    Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>Any one of these narrows an issue search: the full barcode, the UPC part alone, or series words and a number.</summary>
public sealed record IssueSearch
{
    /// <summary>UPC and add-on as one run of digits (<see cref="ComicBarcode.Value"/>); must match exactly.</summary>
    public string? Barcode { get; init; }

    /// <summary>A UPC without its add-on: every issue whose barcode starts with it.</summary>
    public string? UpcPrefix { get; init; }

    /// <summary>Words that must all be in the series name.</summary>
    public string? Series { get; init; }

    public int? SeriesYear { get; init; }

    public string? Number { get; init; }
}

/// <param name="Details">The issue with its regular cover.</param>
/// <param name="Variants">Its other covers.</param>
public sealed record IssueFacts(ComicDetails Details, IReadOnlyList<VariantFacts> Variants);

/// <param name="Name">e.g. "Cover B Fiona Staples Variant".</param>
/// <param name="Barcode">The variant's own barcode, with its add-on, if it has one.</param>
public sealed record VariantFacts(string Name, string? Barcode, string? CoverUrl);
