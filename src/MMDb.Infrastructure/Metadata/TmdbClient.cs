using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MMDb.Application.Metadata;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Metadata;

/// <summary>TMDB settings. The key is set in the PC's Settings screen (or its user secrets, <c>Tmdb:ApiKey</c>), never in the repo.</summary>
public sealed class TmdbSettings
{
    /// <summary>Either the "API Read Access Token" (sent as a bearer token) or the older "API Key".</summary>
    public string? ApiKey { get; set; }

    public string Language { get; set; } = "en-US";

    /// <summary>Whose age rating to use ("R", "PG-13" for the US).</summary>
    public string Region { get; set; } = "US";
}

/// <summary><see cref="IMovieMetadata"/> over TMDB's v3 API.</summary>
internal sealed class TmdbClient(HttpClient http, TmdbSettings settings) : IMovieMetadata
{
    public const string Source = "TMDB";

    public const string BaseAddress = "https://api.themoviedb.org/3/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    // Theatrical first, then limited, premiere and home releases.
    private static readonly int[] ReleaseTypePreference = [3, 2, 1, 4, 5, 6];

    public async Task<IReadOnlyList<int>> SearchAsync(string title, int? year, CancellationToken cancellationToken = default) =>
        [.. (await FindAsync(title, year, cancellationToken)).Select(hit => hit.TmdbId)];

    public async Task<IReadOnlyList<MovieHit>> FindAsync(string title, int? year, CancellationToken cancellationToken = default)
    {
        var query = $"search/movie?query={Uri.EscapeDataString(title)}&include_adult=false&language={settings.Language}&page=1"
            + (year is { } y ? string.Create(CultureInfo.InvariantCulture, $"&year={y}") : "");
        var page = await GetAsync<SearchPage>(query, cancellationToken);
        return [.. (page?.Results ?? []).Select(r => new MovieHit(
            r.Id, r.Title ?? "", YearOf(r.ReleaseDate), r.PosterPath, r.OriginalTitle is { Length: > 0 } original && original != r.Title ? original : null))];
    }

    public async Task<MovieDetails?> GetAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var movie = await GetAsync<TmdbMovie>(
            string.Create(CultureInfo.InvariantCulture, $"movie/{tmdbId}?append_to_response=credits,videos,release_dates&language={settings.Language}"),
            cancellationToken);
        return movie?.ToDetails(settings.Region);
    }

    /// <summary>A GET carrying the key the way TMDB takes that kind: a read access token as a bearer token, an older key in the query.</summary>
    internal static HttpRequestMessage Request(string uri, string key)
    {
        key = key.Trim();
        if (key.StartsWith("eyJ", StringComparison.Ordinal)) // read access tokens are JWTs
        {
            return new(HttpMethod.Get, uri) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", key) } };
        }

        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new(HttpMethod.Get, $"{uri}{separator}api_key={Uri.EscapeDataString(key)}");
    }

    /// <returns><see langword="null"/> for 404 Not Found.</returns>
    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new MetadataException(Source, MetadataProblem.NotConfigured);
        }

        using var request = Request(relativeUri, settings.ApiKey);
        using var response = await Http.SendAsync(http, request, Source, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Unauthorized:
                throw new MetadataException(Source, MetadataProblem.KeyRejected);
            case HttpStatusCode.TooManyRequests:
                throw new MetadataException(Source, MetadataProblem.Busy);
            case { } status when !response.IsSuccessStatusCode:
                throw new MetadataException(Source, MetadataProblem.Unavailable, new HttpRequestException($"TMDB answered {(int)status}.", null, status));
        }

        return await Http.ReadJsonAsync<T>(response, Json, Source, cancellationToken);
    }

    private sealed record SearchPage(List<SearchResult>? Results);

    private sealed record SearchResult(int Id, string? Title, string? OriginalTitle, string? ReleaseDate, string? PosterPath);

    private static int? YearOf(string? releaseDate) =>
        releaseDate is { Length: >= 4 } date && int.TryParse(date.AsSpan(0, 4), CultureInfo.InvariantCulture, out var year) ? year : null;

    private sealed record TmdbMovie(
        int Id,
        string? Title,
        string? ReleaseDate,
        int? Runtime,
        string? Overview,
        string? PosterPath,
        List<Named>? Genres,
        Credits? Credits,
        Page<Video>? Videos,
        Page<CountryReleases>? ReleaseDates)
    {
        public MovieDetails ToDetails(string region) => new()
        {
            TmdbId = Id,
            Title = Title ?? "",
            Year = YearOf(ReleaseDate),
            RuntimeMinutes = Runtime is > 0 ? Runtime : null,
            Overview = Overview,
            PosterPath = PosterPath,
            Genres = [.. (Genres ?? []).Select(g => g.Name)],
            Directors = [.. (Credits?.Crew ?? []).Where(c => c.Job == "Director").Select(c => c.Name).Distinct()],
            Cast = [.. (Credits?.Cast ?? []).OrderBy(c => c.Order).Select(c => new CastMember(c.Name, c.Character))],
            Crew = [.. (Credits?.Crew ?? [])
                .Where(c => c.Job != "Director" && !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Job))
                .Select(c => new CrewMember(c.Name, c.Job!, c.Department))],
            Rating = Certification(region),
            TrailerYouTubeKey = TrailerKey(),
        };

        private string? Certification(string region) =>
            (ReleaseDates?.Results ?? [])
                .Where(r => string.Equals(r.Iso31661, region, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.ReleaseDates ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Certification))
                .OrderBy(r => Array.IndexOf(ReleaseTypePreference, r.Type) is var i and >= 0 ? i : int.MaxValue)
                .Select(r => r.Certification!.Trim())
                .FirstOrDefault();

        // YouTube trailers, official ones first, then the newest.
        private string? TrailerKey() =>
            (Videos?.Results ?? [])
                .Where(v => v.Site == "YouTube" && v.Type == "Trailer" && !string.IsNullOrWhiteSpace(v.Key))
                .OrderByDescending(v => v.Official)
                .ThenByDescending(v => v.PublishedAt)
                .Select(v => v.Key)
                .FirstOrDefault();
    }

    private sealed record Named(string Name);

    private sealed record Credits(List<CastCredit>? Cast, List<CrewCredit>? Crew);

    private sealed record CastCredit(string Name, string? Character, int Order);

    private sealed record CrewCredit(string Name, string? Job, string? Department);

    private sealed record Page<T>(List<T>? Results);

    private sealed record Video(string? Site, string? Type, string? Key, bool Official, DateTimeOffset? PublishedAt);

    private sealed record CountryReleases([property: JsonPropertyName("iso_3166_1")] string Iso31661, List<Release>? ReleaseDates);

    private sealed record Release(string? Certification, int Type);
}
