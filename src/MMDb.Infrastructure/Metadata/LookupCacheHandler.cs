using System.Net;
using System.Text;
using System.Web;
using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure.Metadata;

/// <summary>
/// Answers repeated GET requests from the lookup cache. Only successful answers are kept, each for
/// <c>maxAge</c> (which depends on the request: search results go stale sooner than a film's details).
/// </summary>
internal sealed class LookupCacheHandler(string provider, Func<Uri, TimeSpan> maxAge, LookupCache cache, TimeProvider clock)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } uri)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var key = CacheKey(uri);
        var now = clock.GetUtcNow();
        if (await cache.FindAsync(provider, key, now - maxAge(uri), cancellationToken) is { } cached)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(cached), RequestMessage = request };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            await cache.StoreAsync(provider, key, json, now, cancellationToken);
            response.Content.Dispose();
            response.Content = Json(json);
        }

        return response;
    }

    /// <summary>Path and query, with any <c>api_key</c> left out so keys never reach the database.</summary>
    internal static string CacheKey(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        query.Remove("api_key");
        var rest = query.ToString();
        return uri.AbsolutePath + (string.IsNullOrEmpty(rest) ? "" : "?" + rest);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
