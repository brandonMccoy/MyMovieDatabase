using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Wishlist;

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
                ("director", request.Director),
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

    public Task<WishlistResponse> ListWishlistAsync(string? search = null, CancellationToken cancellationToken = default) =>
        GetAsync<WishlistResponse>("wishlist" + Query(("search", search)), cancellationToken);

    public Task<WishlistMovieDto?> GetWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<WishlistMovieDto>($"wishlist/movies/{id}", cancellationToken);

    public Task<WishlistComicDto?> GetWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<WishlistComicDto>($"wishlist/comics/{id}", cancellationToken);

    public Task<WantMovieResponse> ChangeWishlistFormatAsync(Guid id, MovieFormatKind format, CancellationToken cancellationToken = default) =>
        SaveAsync<WantMovieResponse>(HttpMethod.Put, $"wishlist/movies/{id}/format", new ChangeWishFormatRequest(format), cancellationToken);

    public Task<WantMovieResponse> WantMovieAsync(WantMovieRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<WantMovieResponse>(HttpMethod.Post, "wishlist/movies", request, cancellationToken);

    public Task<bool> RemoveWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"wishlist/movies/{id}", cancellationToken);

    public Task<MovieLookupResponse?> LookupWishlistMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<MovieLookupResponse>($"wishlist/movies/{id}/lookup", cancellationToken);

    public Task<WantComicResponse> WantComicAsync(WantComicRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<WantComicResponse>(HttpMethod.Post, "wishlist/comics", request, cancellationToken);

    public Task<WantComicResponse> WantMissingIssuesAsync(WantMissingRequest request, CancellationToken cancellationToken = default) =>
        SaveAsync<WantComicResponse>(HttpMethod.Post, "wishlist/comics/missing", request, cancellationToken);

    public Task<bool> RemoveWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        DeleteAsync($"wishlist/comics/{id}", cancellationToken);

    public Task<ComicLookupResponse?> LookupWishlistComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAsync<ComicLookupResponse>($"wishlist/comics/{id}/lookup", cancellationToken);

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
