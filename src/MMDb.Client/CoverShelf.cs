using MMDb.Contracts;
using MMDb.Contracts.Phone;

namespace MMDb.Client;

/// <param name="ContentType">E.g. <c>image/jpeg</c>.</param>
public sealed record CoverImage(byte[] Bytes, string ContentType);

/// <summary>
/// Covers for the phone's screens, in a folder on the phone. What the PC has is fetched from it once, as a
/// smaller copy, and kept; the cover of something saved on the phone is put there by the save itself. A
/// cover's name changes whenever its picture does, so a kept file never goes stale.
/// </summary>
/// <param name="directory">The phone's folder for covers.</param>
public sealed class CoverShelf(PcLink pc, string directory)
{
    // Enough at once to be quick on Wi-Fi without swamping the PC.
    private const int AtOnce = 6;

    private const string FullFolder = "full";

    /// <param name="full">
    /// For a page that shows one cover large: the PC's own file, while the PC is in reach (kept too, under
    /// <c>full</c>). Away from it, the smaller copy does.
    /// </param>
    /// <returns><see langword="null"/> if there is no such cover, or the PC can't be reached and it isn't kept yet.</returns>
    public async Task<CoverImage?> GetAsync(string name, bool full = false, CancellationToken cancellationToken = default)
    {
        if (!IsName(name))
        {
            return null;
        }

        return full && await FetchAsync(name, Path.Combine(directory, FullFolder), size: null, onlyIfConnected: true, cancellationToken) is { } large
            ? large
            : await FetchAsync(name, directory, size: "medium", onlyIfConnected: false, cancellationToken);
    }

    private async Task<CoverImage?> FetchAsync(string name, string folder, string? size, bool onlyIfConnected, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, name);
        var contentType = ContentTypeOf(name);
        try
        {
            if (File.Exists(path))
            {
                return new(await File.ReadAllBytesAsync(path, cancellationToken), contentType);
            }

            // A PC that is away takes seconds not to answer: a page doesn't wait on that for a sharper cover.
            if (onlyIfConnected && pc.State != PcLinkState.Connected)
            {
                return null;
            }

            using var response = await pc.SendAsync(HttpMethod.Get, "covers/" + Uri.EscapeDataString(name) + (size is null ? "" : "?size=" + size), content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Directory.CreateDirectory(folder);
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

    /// <summary>
    /// Has the folder hold just these covers: the ones missing are fetched from the PC, so they are there
    /// away from home, and files for covers no longer shown are removed.
    /// </summary>
    /// <returns>How many were fetched.</returns>
    public async Task<int> KeepAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var wanted = names.Where(IsName).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(directory))
        {
            var full = Path.Combine(directory, FullFolder);
            foreach (var file in Directory.EnumerateFiles(directory).Concat(Directory.Exists(full) ? Directory.EnumerateFiles(full) : []))
            {
                // Not a file from the last hour: it may be the cover of something being saved right now.
                if (!wanted.Contains(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1))
                {
                    File.Delete(file);
                }
            }
        }

        var fetched = 0;
        var missing = wanted.Where(name => !File.Exists(Path.Combine(directory, name))).ToList();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = cancellationToken }, async (name, ct) =>
        {
            if (await GetAsync(name, full: false, ct) is not null)
            {
                Interlocked.Increment(ref fetched);
            }
        });
        return fetched;
    }

    private static bool IsName(string? name) =>
        !string.IsNullOrEmpty(name) && Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not ("." or "..");

    private static string ContentTypeOf(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".PNG" => "image/png",
        ".WEBP" => "image/webp",
        ".GIF" => "image/gif",
        _ => "image/jpeg",
    };
}
