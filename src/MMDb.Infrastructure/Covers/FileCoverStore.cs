using MMDb.Application.Metadata;

namespace MMDb.Infrastructure.Covers;

/// <summary>Covers as files in one folder (<c>%LOCALAPPDATA%\Shelf\covers</c> on the PC).</summary>
internal sealed class FileCoverStore(string directory) : ICoverStore
{
    public string Directory { get; } = directory;

    public async Task SaveAsync(string name, byte[] image, CancellationToken cancellationToken = default)
    {
        var path = PathOf(name);
        System.IO.Directory.CreateDirectory(Directory);

        // Write beside it and swap in, so a half-written file is never shown.
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, image, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (IOException)
        {
            // In use or already gone: a stray cover file does no harm.
        }
    }

    private string PathOf(string name) =>
        name.Length > 0 && Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not ("." or "..")
            ? Path.Combine(Directory, name)
            : throw new ArgumentException($"'{name}' is not a plain file name.", nameof(name));
}
