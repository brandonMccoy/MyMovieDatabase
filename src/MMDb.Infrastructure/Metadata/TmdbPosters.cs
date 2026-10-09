using MMDb.Application.Metadata;

namespace MMDb.Infrastructure.Metadata;

/// <summary>TMDB's image server.</summary>
public static class TmdbImages
{
    /// <summary>The size the PC keeps: sharp on the detail page, about 60–90 KB a poster.</summary>
    public const string StoredSize = "w500";

    /// <summary>Small enough for a list of search results.</summary>
    public const string ThumbnailSize = "w92";

    /// <summary>For the poster on the add/edit screen before it has been downloaded.</summary>
    public const string PreviewSize = "w342";

    private static readonly Uri BaseAddress = new("https://image.tmdb.org/t/p/");

    /// <summary>Where a poster can be loaded straight from TMDB, e.g. in search results before anything is saved.</summary>
    /// <param name="posterPath">See <see cref="Domain.Movies.MovieDetails.PosterPath"/>.</param>
    public static Uri Url(string posterPath, string size) => new(BaseAddress, size + posterPath);
}

/// <summary>Downloads posters at <see cref="TmdbImages.StoredSize"/>.</summary>
internal sealed class TmdbPosters(HttpClient http) : IPosterSource
{
    public async Task<byte[]?> FetchAsync(string posterPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(TmdbImages.Url(posterPath, TmdbImages.StoredSize), cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancellationToken) : null;
        }
        catch (Exception ex) when (Http.IsTransportFailure(ex, cancellationToken))
        {
            return null;
        }
    }
}
