using System.Net;
using MMDb.Application.Metadata;
using MMDb.Domain;

namespace MMDb.Infrastructure.Metadata;

/// <summary>
/// <see cref="IBarcodeLookup"/> over UPCitemdb's free tier: no key, 100 lookups a day, 6 a minute.
/// </summary>
internal sealed class UpcItemDbClient(HttpClient http) : IBarcodeLookup
{
    public const string Source = "UPCitemdb";

    public async Task<IReadOnlyList<string>> FindProductNamesAsync(Upc upc, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"prod/trial/lookup?upc={upc.Value}");
        using var response = await Http.SendAsync(http, request, Source, cancellationToken);
        switch (response.StatusCode)
        {
            // INVALID_UPC: a barcode it can't take (we only send valid check digits) is as good as unknown.
            case HttpStatusCode.BadRequest or HttpStatusCode.NotFound:
                return [];
            case HttpStatusCode.TooManyRequests:
                throw new MetadataException(Source, MetadataProblem.Busy);
            case { } status when !response.IsSuccessStatusCode:
                throw new MetadataException(Source, MetadataProblem.Unavailable, new HttpRequestException($"UPCitemdb answered {(int)status}.", null, status));
        }

        var page = await Http.ReadJsonAsync<LookupPage>(response, TmdbClient.Json, Source, cancellationToken);
        return [.. (page?.Items ?? [])
            .Select(i => i.Title?.Trim())
            .OfType<string>()
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private sealed record LookupPage(List<Item>? Items);

    private sealed record Item(string? Title);
}
