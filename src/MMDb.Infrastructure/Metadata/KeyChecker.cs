using System.Net;
using MMDb.Application.Metadata;

namespace MMDb.Infrastructure.Metadata;

/// <summary><see cref="IKeyChecker"/>: one small request a check, never answered from the lookup cache.</summary>
internal sealed class KeyChecker(HttpClient http) : IKeyChecker
{
    public Task<KeyCheck> CheckTmdbAsync(string key, CancellationToken cancellationToken = default) =>
        AskAsync(TmdbClient.Request(TmdbClient.BaseAddress + "authentication", key), cancellationToken);

    // Metron has nothing made for this; a search for a barcode nothing carries is its smallest answer.
    public Task<KeyCheck> CheckMetronAsync(string token, CancellationToken cancellationToken = default) =>
        AskAsync(MetronClient.Request(MetronClient.BaseAddress + "issue/?upc=000000000000", token), cancellationToken);

    private async Task<KeyCheck> AskAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => KeyCheck.Rejected,
                    _ when response.IsSuccessStatusCode => KeyCheck.Accepted,
                    _ => KeyCheck.Unknown,
                };
            }
            catch (Exception ex) when (Http.IsTransportFailure(ex, cancellationToken))
            {
                return KeyCheck.Unknown;
            }
        }
    }
}
