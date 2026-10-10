using System.Runtime.CompilerServices;

namespace MMDb.Infrastructure.Tests;

/// <summary>
/// Approval testing for generated files: the text must match <c>Snapshots/&lt;name&gt;.verified.&lt;ext&gt;</c>.
/// Otherwise the test fails and leaves <c>.received.&lt;ext&gt;</c> beside it; if the new text is right, rename it.
/// </summary>
internal static class Snapshot
{
    public static void Match(string text, string name, string extension, [CallerFilePath] string caller = "")
    {
        var folder = Path.Combine(Path.GetDirectoryName(caller)!, "Snapshots");
        var verified = Path.Combine(folder, $"{name}.verified.{extension}");
        var received = Path.Combine(folder, $"{name}.received.{extension}");
        if (File.Exists(verified) && File.ReadAllText(verified).ReplaceLineEndings("\n") == text.ReplaceLineEndings("\n"))
        {
            File.Delete(received);
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(received, text);
        Assert.Fail(File.Exists(verified)
            ? $"{name} changed. Compare {received} with {verified}; if the change is right, rename it over the .verified file."
            : $"{name} has no snapshot yet. Check {received} and rename it to .verified.{extension}.");
    }
}
