namespace MMDb.Testing;

/// <summary>An empty folder of its own under the temp directory, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shelf-tests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string this[string relative] => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }
}
