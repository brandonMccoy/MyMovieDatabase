using MMDb.Application.Comics;
using MMDb.Application.Metadata;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Domain.Comics;
using MMDb.Server.Movies;

namespace MMDb.Server.Comics;

/// <summary>Translates between the comic contracts and the domain.</summary>
internal static class ComicMapping
{
    public static ComicFormatKind ToContract(this ComicFormat format) => format switch
    {
        ComicFormat.SingleIssue => ComicFormatKind.SingleIssue,
        ComicFormat.TradePaperback => ComicFormatKind.TradePaperback,
        ComicFormat.Hardcover => ComicFormatKind.Hardcover,
        ComicFormat.Omnibus => ComicFormatKind.Omnibus,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    // Unknown values pass through as undefined formats, which validation reports.
    public static ComicFormat ToDomain(this ComicFormatKind format) => format switch
    {
        ComicFormatKind.SingleIssue => ComicFormat.SingleIssue,
        ComicFormatKind.TradePaperback => ComicFormat.TradePaperback,
        ComicFormatKind.Hardcover => ComicFormat.Hardcover,
        ComicFormatKind.Omnibus => ComicFormat.Omnibus,
        _ => (ComicFormat)(-1),
    };

    public static ComicQuery ToDomain(this ComicListRequest request) => new()
    {
        Search = request.Search,
        View = request.Shelf switch
        {
            ComicShelfKind.Issues => ComicView.Issues,
            ComicShelfKind.Collected => ComicView.Collected,
            ComicShelfKind.KeyIssues => ComicView.KeyIssues,
            _ => ComicView.Series,
        },
        Publisher = string.IsNullOrWhiteSpace(request.Publisher) ? null : request.Publisher,
        Sort = request.Sort == ComicSortOrder.RecentlyAdded ? ComicSort.RecentlyAdded : ComicSort.Series,
    };

    public static ComicDetails ToDetails(this SaveComicRequest request) => new()
    {
        Format = request.Format.ToDomain(),
        Series = request.Series,
        SeriesYear = request.SeriesYear,
        Number = request.Number,
        Title = request.Title,
        Variant = request.Variant,
        Printing = request.Printing,
        Publisher = request.Publisher,
        CoverDate = request.CoverDate,
        Writers = request.Writers ?? [],
        Artists = request.Artists ?? [],
        Summary = request.Summary,
        Barcode = request.Barcode,
        Isbn = request.Isbn,
        MetronId = request.MetronId,
        MetronSeriesId = request.MetronSeriesId,
        IsKeyIssue = request.IsKeyIssue,
        KeyNote = request.KeyNote,
        CoverUrl = request.CoverSource,
    };

    public static SaveComicRequest ToRequest(this ComicDetails details) => new()
    {
        Format = details.Format.ToContract(),
        Series = details.Series,
        SeriesYear = details.SeriesYear,
        Number = details.Number,
        Title = details.Title,
        Variant = details.Variant,
        Printing = details.Printing,
        Publisher = details.Publisher,
        CoverDate = details.CoverDate,
        Writers = details.Writers,
        Artists = details.Artists,
        Summary = details.Summary,
        Barcode = details.Barcode,
        Isbn = details.Isbn,
        MetronId = details.MetronId,
        MetronSeriesId = details.MetronSeriesId,
        IsKeyIssue = details.IsKeyIssue,
        KeyNote = details.KeyNote,
        CoverSource = details.CoverUrl,
    };

    public static ComicDto ToDto(this Comic comic, CoverLinks covers, SeriesRun? run = null) => new()
    {
        Id = comic.Id,
        Version = comic.Version,
        DisplayName = comic.DisplayName,
        Format = comic.Format.ToContract(),
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
        Barcode = comic.Barcode?.Value,
        Isbn = comic.Isbn?.Value,
        MetronId = comic.MetronId,
        MetronSeriesId = comic.MetronSeriesId,
        IsKeyIssue = comic.IsKeyIssue,
        KeyNote = comic.KeyNote,
        CoverSource = comic.CoverUrl,
        CoverUrl = covers.For(comic.CoverImage),
        CreatedAt = comic.CreatedAt,
        UpdatedAt = comic.UpdatedAt,
        Run = run?.ToContract(covers),
    };

    public static ComicSummaryDto ToSummary(this Comic comic, CoverLinks covers) => new()
    {
        Id = comic.Id,
        DisplayName = comic.DisplayName,
        Format = comic.Format.ToContract(),
        Series = comic.Series,
        SeriesYear = comic.SeriesYear,
        Number = comic.Number,
        Title = comic.Title,
        Variant = comic.Variant,
        Printing = comic.Printing,
        Publisher = comic.Publisher,
        CoverDate = comic.CoverDate,
        IsKeyIssue = comic.IsKeyIssue,
        KeyNote = comic.KeyNote,
        CoverUrl = covers.For(comic.CoverImage),
        CreatedAt = comic.CreatedAt,
    };

    public static SeriesRunDto ToContract(this SeriesRun run, CoverLinks covers) => new(
        run.Name,
        run.Year,
        run.Title,
        run.Publisher,
        [.. run.Entries.Select(e => new RunEntryDto(e.Number, [.. e.Copies.Select(c => c.ToSummary(covers))], e.Through))],
        run.OwnedCount,
        run.KnownCount);

    public static ComicListResponse ToContract(this ComicShelf shelf, CoverLinks covers) => new(
        [.. shelf.Runs.Select(r => r.ToContract(covers))],
        [.. shelf.Comics.Select(c => c.ToSummary(covers))],
        new ComicFacetsDto(shelf.Facets.IssueCount, shelf.Facets.SeriesCount, shelf.Facets.CollectedCount, shelf.Facets.KeyIssueCount, shelf.Facets.Publishers));

    public static SaveComicResponse ToContract(this SaveComicResult result, CoverLinks covers) => result switch
    {
        SaveComicResult.Saved saved => new(SaveStatus.Saved, saved.Comic.ToDto(covers)),
        SaveComicResult.AlreadyOwned owned => new(SaveStatus.AlreadyOwned, owned.Comic.ToDto(covers)),
        SaveComicResult.Invalid invalid => new(SaveStatus.Invalid, Errors: [.. invalid.Errors.Select(e => new FieldError(e.Field, e.Message))]),
        SaveComicResult.NotFound => new(SaveStatus.NotFound),
        SaveComicResult.Conflict => new(SaveStatus.Conflict),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    public static ComicLookupResponse ToContract(this ComicLookupResult result, CoverLinks covers) => new()
    {
        Status = result.Outcome switch
        {
            LookupOutcome.Found => LookupStatus.Found,
            LookupOutcome.NoMatches => LookupStatus.NoMatches,
            LookupOutcome.UnknownBarcode => LookupStatus.UnknownBarcode,
            LookupOutcome.BarcodeOwned => LookupStatus.BarcodeOwned,
            _ => result.Problem switch
            {
                MetadataProblem.NotConfigured => LookupStatus.NotConfigured,
                MetadataProblem.KeyRejected => LookupStatus.KeyRejected,
                MetadataProblem.Busy => LookupStatus.Busy,
                _ => LookupStatus.Unavailable,
            },
        },
        Candidates = [.. result.Matches.Select(m => new ComicCandidateDto
        {
            Details = m.Details.ToRequest(),
            DisplayName = m.Details.DisplayName,
            CoverUrl = Uri.TryCreate(m.Details.CoverUrl, UriKind.Absolute, out var cover) ? cover : null,
            Owned = [.. m.Owned.Select(c => c.ToSummary(covers))],
            IsOwned = m.IsOwned,
        })],
        Barcode = result.Barcode,
        OwnedByCode = result.OwnedByCode?.ToSummary(covers),
        FailedService = result.FailedService,
    };
}
