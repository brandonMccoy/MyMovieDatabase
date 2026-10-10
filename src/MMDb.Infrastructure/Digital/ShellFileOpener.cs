using System.ComponentModel;
using System.Diagnostics;
using MMDb.Application.Digital;

namespace MMDb.Infrastructure.Digital;

/// <summary>Opens a file the way double-clicking it in Explorer does.</summary>
internal sealed class ShellFileOpener : IFileOpener
{
    public bool Open(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // No program is set up for this kind of file, or the file went away just now.
            return false;
        }
    }
}
