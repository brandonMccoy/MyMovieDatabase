using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;

namespace MMDb.Client;

/// <summary>
/// <see cref="ICollectionApi"/> for the phone: every call is a request to the PC over the home network.
/// Calls throw <see cref="PcUnavailableException"/> when the PC can't be reached.
/// </summary>
public sealed class HttpCollectionApi(PcLink pc) : ICollectionApi
{
    /// <summary>Raised for what this phone saved; it isn't told of changes made on the PC.</summary>
    public event Action? Changed;

    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetAsync<MovieListResponse>(
            "movies" + Query(
                ("search", request.Search),
                ("format", request.Format?.ToString()),
                ("genre", request.Genre),
                ("decade", request.Decade?.ToString(CultureInfo.InvariantCulture)),
                ("sort", request.Sort.ToString())),
            cancellationToken);
    }

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<MovieDto>($"movies/{id}", cancellationToken);

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<SaveMovieResponse>(HttpMethod.Post, "movies", request, cancellationToken);

    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<SaveMovieResponse>(HttpMethod.Put, $"movies/{id}?version={version.ToString(CultureInfo.InvariantCulture)}", request, cancellationToken);

    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"movies/{id}", cancellationToken);

    public Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<MovieLookupResponse>("movies/lookup" + Query(("q", query)), cancellationToken);

    public Task<ComicListResponse> ListComicsAsync(ComicListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetAsync<ComicListResponse>(
            "comics" + Query(
                ("search", request.Search),
                ("shelf", request.Shelf.ToString()),
                ("publisher", request.Publisher),
                ("sort", request.Sort.ToString())),
            cancellationToken);
    }

    public Task<ComicDto?> GetComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<ComicDto>($"comics/{id}", cancellationToken);

    public Task<SaveComicResponse> AddComicAsync(SaveComicRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<SaveComicResponse>(HttpMethod.Post, "comics", request, cancellationToken);

    public Task<SaveComicResponse> UpdateComicAsync(Guid id, int version, SaveComicRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<SaveComicResponse>(HttpMethod.Put, $"comics/{id}?version={version.ToString(CultureInfo.InvariantCulture)}", request, cancellationToken);

    public Task<bool> DeleteComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"comics/{id}", cancellationToken);

    public Task<ComicLookupResponse> LookupComicAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<ComicLookupResponse>("comics/lookup" + Query(("q", query)), cancellationToken);

    public async Task<ScanResponse> LookupScanAsync(ScanRequest request, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(request, options: ShelfJson.Options);
        using var response = await pc.SendAsync(HttpMethod.Post, "scans", content, cancellationToken);
        return await ReadAsync<ScanResponse>(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await pc.SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> FindAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await pc.SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<T>(response, cancellationToken);
    }

    // The PC answers a save that didn't go through (400, 404, 409) with the same response the PC's own screens get.
    private async Task<T> SaveAsync<T>(HttpMethod method, string path, object request, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(request, request.GetType(), options: ShelfJson.Options);
        using var response = await pc.SendAsync(method, path, content, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict
            && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            return await response.Content.ReadFromJsonAsync<T>(ShelfJson.Options, cancellationToken) ?? throw Unexpected(response);
        }

        var saved = await ReadAsync<T>(response, cancellationToken);
        Changed?.Invoke();
        return saved;
    }

    private async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await pc.SendAsync(HttpMethod.Delete, path, content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Unexpected(response);
        }

        Changed?.Invoke();
        return true;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<T>(ShelfJson.Options, cancellationToken) is { } value
            ? value
            : throw Unexpected(response);

    private static HttpRequestException Unexpected(HttpResponseMessage response) =>
        new($"The PC answered {(int)response.StatusCode} to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}.", null, response.StatusCode);

    private static string Query(params (string Name, string? Value)[] values)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            }
        }

        return query.ToString();
    }
}
