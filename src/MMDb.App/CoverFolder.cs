namespace MMDb.App;

/// <summary>
/// Stored covers are files on the PC; the WebView serves that folder under a host name of its own so the
/// pages can show them with plain <c>&lt;img&gt;</c> tags.
/// </summary>
internal static class CoverFolder
{
    // ".example" is reserved, so it can never clash with a real site.
    public const string HostName = "covers.shelf.example";

    public static readonly Uri BaseUri = new($"https://{HostName}/");

    /// <summary>Set at startup on Windows.</summary>
    public static string? Path { get; set; }
}
