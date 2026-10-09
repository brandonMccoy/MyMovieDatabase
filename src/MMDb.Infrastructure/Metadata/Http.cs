using System.Net.Http.Json;
using System.Text.Json;
using MMDb.Application.Metadata;
using Polly;

namespace MMDb.Infrastructure.Metadata;

/// <summary>Turns transport failures into <see cref="MetadataException"/>s the use cases understand.</summary>
internal static class Http
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, string source, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new MetadataException(source, MetadataProblem.Unavailable, ex);
        }
    }

    public static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, JsonSerializerOptions options, string source, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new MetadataException(source, MetadataProblem.Unavailable, ex);
        }
    }

    /// <summary>Offline, timed out (a timeout is a cancellation the caller didn't ask for) or given up by the resilience pipeline.</summary>
    public static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or ExecutionRejectedException or TimeoutException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
