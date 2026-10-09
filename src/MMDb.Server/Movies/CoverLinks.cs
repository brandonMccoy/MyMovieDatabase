namespace MMDb.Server.Movies;

/// <summary>Where the UI loads stored covers from (on the PC, a WebView host name mapped to the cover folder).</summary>
public sealed class CoverLinks(Uri baseUri)
{
    private readonly Uri _baseUri = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");

    public Uri? For(string? coverImage) => coverImage is null ? null : new Uri(_baseUri, Uri.EscapeDataString(coverImage));
}
