using System.Globalization;
using System.Net;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Infrastructure.Metadata;

/// <summary>
/// <see cref="IBookMetadata"/> over Open Library's Books API: no key; it asks callers to name themselves in the
/// User-Agent, and allows about one request a second.
/// </summary>
internal sealed class OpenLibraryClient(HttpClient http) : IBookMetadata
{
    public const string Source = ComicLookup.OpenLibraryService;

    private static readonly string[] DateFormats = ["MMMM d, yyyy", "MMM d, yyyy", "d MMMM yyyy", "yyyy-MM-dd", "MMMM yyyy", "MMM yyyy", "yyyy-MM"];

    public async Task<ComicDetails?> FindByIsbnAsync(Isbn isbn, CancellationToken cancellationToken = default)
    {
        var key = $"ISBN:{isbn.Value}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/books?bibkeys={key}&jscmd=data&format=json");
        using var response = await Http.SendAsync(http, request, Source, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden:
                throw new MetadataException(Source, MetadataProblem.Busy);
            case { } status when !response.IsSuccessStatusCode:
                throw new MetadataException(Source, MetadataProblem.Unavailable, new HttpRequestException($"Open Library answered {(int)status}.", null, status));
        }

        var books = await Http.ReadJsonAsync<Dictionary<string, Book>>(response, TmdbClient.Json, Source, cancellationToken);
        return books is not null && books.TryGetValue(key, out var book) ? book.ToDetails(isbn) : null;
    }

    private sealed record Named(string? Name);

    private sealed record Covers(string? Large, string? Medium);

    private sealed record Book(string? Title, string? Subtitle, List<Named>? Authors, List<Named>? Publishers, string? PublishDate, Covers? Cover)
    {
        public ComicDetails ToDetails(Isbn isbn) => new ComicDetails
        {
            Format = ComicFormat.TradePaperback,
            Title = string.IsNullOrWhiteSpace(Subtitle) ? Title : $"{Title}, {Subtitle}",
            Publisher = Publishers?.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            Writers = [.. (Authors ?? []).Select(a => a.Name).OfType<string>()],
            CoverDate = DateTime.TryParseExact(PublishDate?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DateOnly.FromDateTime(date) : null,
            Isbn = isbn.Value,
            CoverUrl = Cover?.Large ?? Cover?.Medium,
        }.Normalized();
    }
}

/// <summary>
/// Downloads comic covers, only from the comic databases' own image servers (the links come from their
/// answers, and can be edited).
/// </summary>
internal sealed class ComicCovers(HttpClient http) : ICoverSource
{
    /// <summary>Larger than any cover; anything bigger isn't one.</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase) { "static.metron.cloud", "metron.cloud", "covers.openlibrary.org" };

    public async Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !Hosts.Contains(uri.Host))
        {
            return null;
        }

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true
                || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return image.Length is > 0 and <= MaxBytes ? image : null;
        }
        catch (Exception ex) when (Http.IsTransportFailure(ex, cancellationToken))
        {
            return null;
        }
    }
}
