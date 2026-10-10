using MMDb.Contracts;

namespace MMDb.Client;

/// <param name="ContentType">E.g. <c>image/jpeg</c>.</param>
public sealed record CoverImage(byte[] Bytes, string ContentType);

/// <summary>
/// Covers for the phone's screens: fetched from the PC once and kept in a folder on the phone. A cover's
/// name changes whenever its picture does, so a kept file never goes stale.
/// </summary>
/// <param name="directory">The phone's cache folder for covers.</param>
public sealed class CoverShelf(PcLink pc, string directory)
{
    /// <returns><see langword="null"/> if there is no such cover, or the PC can't be reached and it isn't kept yet.</returns>
    public async Task<CoverImage?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
        {
            return null;
        }

        var path = Path.Combine(directory, name);
        var contentType = ContentTypeOf(name);
        try
        {
            if (File.Exists(path))
            {
                return new(await File.ReadAllBytesAsync(path, cancellationToken), contentType);
            }

            using var response = await pc.SendAsync(HttpMethod.Get, "covers/" + Uri.EscapeDataString(name), content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Directory.CreateDirectory(directory);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, path, overwrite: true);
            return new(bytes, response.Content.Headers.ContentType?.MediaType ?? contentType);
        }
        catch (Exception e) when (e is PcUnavailableException or IOException or HttpRequestException)
        {
            return null;
        }
    }

    private static string ContentTypeOf(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".PNG" => "image/png",
        ".WEBP" => "image/webp",
        ".GIF" => "image/gif",
        _ => "image/jpeg",
    };
}
