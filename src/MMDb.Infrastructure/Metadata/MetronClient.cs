using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MMDb.Application.Metadata;
using MMDb.Domain.Comics;

namespace MMDb.Infrastructure.Metadata;

/// <summary>Metron settings. The API token lives in user secrets on the PC (<c>Metron:Token</c>), never in the repo.</summary>
public sealed class MetronSettings
{
    /// <summary>Made on metron.cloud under your account's API tokens.</summary>
    public string? Token { get; set; }
}

/// <summary><see cref="IComicMetadata"/> over Metron's REST API (20 requests a minute, 5,000 a day).</summary>
internal sealed class MetronClient(HttpClient http, MetronSettings settings) : IComicMetadata
{
    public const string Source = ComicLookup.MetronService;

    /// <summary>Pages of a series' issue list read at most (100 issues a page).</summary>
    public const int MaxPages = 20;

    // Credits that count as writing or as art; letterers, editors and the like are left out.
    private static readonly HashSet<string> WriterRoles = new(StringComparer.OrdinalIgnoreCase) { "Writer", "Script", "Scripter", "Story", "Plot", "Plotter" };
    private static readonly HashSet<string> ArtistRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Artist", "Penciller", "Pencils", "Inker", "Inks", "Colorist", "Colors", "Cover", "Cover Artist", "Illustrator", "Painter",
    };

    public async Task<IReadOnlyList<int>> SearchAsync(IssueSearch search, CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder("issue/?");
        Append(query, "upc", search.Barcode);
        Append(query, "upc_starts_with", search.UpcPrefix);
        Append(query, "series_name", search.Series);
        Append(query, "series_year_began", search.SeriesYear?.ToString(CultureInfo.InvariantCulture));
        Append(query, "number", search.Number);
        var page = await GetAsync<Page<IssueSummary>>(query.ToString().TrimEnd('&'), cancellationToken);
        return [.. (page?.Results ?? []).Select(r => r.Id)];
    }

    public async Task<IssueFacts?> GetIssueAsync(int metronId, CancellationToken cancellationToken = default)
    {
        var issue = await GetAsync<Issue>(string.Create(CultureInfo.InvariantCulture, $"issue/{metronId}/"), cancellationToken);
        return issue?.ToFacts();
    }

    public async Task<SeriesFacts?> GetSeriesAsync(int metronSeriesId, CancellationToken cancellationToken = default)
    {
        var series = await GetAsync<Series>(string.Create(CultureInfo.InvariantCulture, $"series/{metronSeriesId}/"), cancellationToken);
        if (series is null)
        {
            return null;
        }

        var issues = await ListIssuesAsync(metronSeriesId, cancellationToken);
        return new SeriesFacts(series.Name ?? "", series.YearBegan, series.Publisher?.Name, [.. issues.Select(i => i.Number)]);
    }

    public async Task<IReadOnlyList<IssueListing>> ListIssuesAsync(int metronSeriesId, CancellationToken cancellationToken = default)
    {
        var issues = new List<IssueListing>();
        string? next = string.Create(CultureInfo.InvariantCulture, $"issue/?series_id={metronSeriesId}");
        for (var pages = 0; next is not null && pages < MaxPages; pages++)
        {
            var page = await GetAsync<Page<IssueSummary>>(next, cancellationToken);
            issues.AddRange((page?.Results ?? []).Where(r => r.Number is not null).Select(r => new IssueListing(r.Id, r.Number!, Https(r.Image))));
            next = page?.Next;
        }

        return issues;
    }

    private static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private static void Append(StringBuilder query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Append(key).Append('=').Append(Uri.EscapeDataString(value.Trim())).Append('&');
        }
    }

    /// <returns><see langword="null"/> for 404 Not Found.</returns>
    private async Task<T?> GetAsync<T>(string uri, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            throw new MetadataException(Source, MetadataProblem.NotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token.Trim());

        using var response = await Http.SendAsync(http, request, Source, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new MetadataException(Source, MetadataProblem.KeyRejected);
            case HttpStatusCode.TooManyRequests:
                throw new MetadataException(Source, MetadataProblem.Busy);
            case { } status when !response.IsSuccessStatusCode:
                throw new MetadataException(Source, MetadataProblem.Unavailable, new HttpRequestException($"Metron answered {(int)status}.", null, status));
        }

        return await Http.ReadJsonAsync<T>(response, TmdbClient.Json, Source, cancellationToken);
    }

    private sealed record Page<T>(string? Next, List<T>? Results);

    private sealed record IssueSummary(int Id, string? Number, string? Image = null);

    private sealed record Named(int Id, string? Name);

    private sealed record SeriesRef(int Id, string? Name, int? YearBegan, Named? SeriesType);

    private sealed record Credit(string? Creator, List<Named>? Role);

    private sealed record Variant(string? Name, string? Upc, string? Image);

    private sealed record Series(int Id, string? Name, int? YearBegan, Named? Publisher);

    private sealed record Issue(
        int Id,
        Named? Publisher,
        SeriesRef? Series,
        string? Number,
        string? Title,
        List<string>? Name,
        string? CoverDate,
        string? Upc,
        string? Isbn,
        string? Desc,
        string? Image,
        List<Credit>? Credits,
        List<Variant>? Variants)
    {
        public IssueFacts ToFacts()
        {
            var format = FormatOf(Series?.SeriesType?.Name);
            var details = new ComicDetails
            {
                Format = format,
                Series = Series?.Name,
                SeriesYear = Series?.YearBegan,
                Number = Number,
                Title = TitleFor(format),
                Publisher = Publisher?.Name,
                CoverDate = DateOnly.TryParseExact(CoverDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
                Writers = Credited(WriterRoles),
                Artists = Credited(ArtistRoles),
                Summary = Desc,
                Barcode = ComicBarcode.TryParse(Upc, out var barcode) ? barcode.Value : null,
                Isbn = Domain.Isbn.TryParse(Isbn, out var isbn) ? isbn.Value : null,
                MetronId = Id,
                MetronSeriesId = Series?.Id,
                CoverUrl = Https(Image),
            };

            var variants = (Variants ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .Select(v => new VariantFacts(v.Name!.Trim(), ComicBarcode.TryParse(v.Upc, out var b) ? b.Value : null, Https(v.Image)))
                .ToList();
            return new IssueFacts(details.Normalized(), variants);
        }

        // Metron lists trades, hardcovers and omnibuses as series of their own, told apart by the series type.
        private static ComicFormat FormatOf(string? seriesType) => seriesType?.Trim().ToUpperInvariant() switch
        {
            "TRADE PAPERBACK" => ComicFormat.TradePaperback,
            "HARDCOVER" or "HARD COVER" => ComicFormat.Hardcover,
            "OMNIBUS" => ComicFormat.Omnibus,
            _ => ComicFormat.SingleIssue,
        };

        // An issue's story title is the first of its story names; a trade's title is "Volume Seven", so it is
        // given its series: "Saga, Volume Seven".
        private string? TitleFor(ComicFormat format)
        {
            var title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim();
            if (!format.IsCollected())
            {
                return title ?? Name?.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            }

            return title is null || Series?.Name is not { } series || title.Contains(series, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{series}, {title}";
        }

        private string[] Credited(HashSet<string> roles) =>
            [.. (Credits ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Creator) && (c.Role ?? []).Any(r => r.Name is not null && roles.Contains(r.Name)))
                .Select(c => c.Creator!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
