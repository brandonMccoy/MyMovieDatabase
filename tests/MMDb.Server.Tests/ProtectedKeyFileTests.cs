using System.Runtime.Versioning;
using System.Text;
using MMDb.Application.Metadata;
using MMDb.Server.Keys;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The file the PC keeps its keys in, with Windows doing the encrypting for real.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedKeyFileTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string Path => _folder["keys.dat"];

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Before_anything_is_saved_there_are_no_keys()
    {
        new ProtectedKeyFile(Path).Load().ShouldBeNull();
    }

    [Fact]
    public void Saved_keys_come_back_after_a_restart()
    {
        new ProtectedKeyFile(Path).Save(new("tmdb-key-0001", null));

        new ProtectedKeyFile(Path).Load().ShouldBe(new SavedKeys("tmdb-key-0001", null));
    }

    [Fact]
    public void Saving_again_replaces_what_was_there()
    {
        var file = new ProtectedKeyFile(Path);
        file.Save(new("tmdb-key-0001", "metron-token-0002"));

        file.Save(new(null, "metron-token-0003"));

        file.Load().ShouldBe(new SavedKeys(null, "metron-token-0003"));
        Directory.GetFiles(_folder.Path).ShouldBe([Path]);
    }

    [Fact]
    public void The_file_cant_be_read_as_text()
    {
        new ProtectedKeyFile(Path).Save(new("tmdb-key-0001", "metron-token-0002"));

        var bytes = File.ReadAllBytes(Path);

        foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
        {
            var text = encoding.GetString(bytes);
            text.ShouldNotContain("tmdb-key-0001");
            text.ShouldNotContain("metron-token-0002");
        }
    }

    [Fact]
    public void A_file_windows_cant_decrypt_counts_as_no_keys()
    {
        File.WriteAllText(Path, """{"Tmdb":"copied from another PC"}""");

        new ProtectedKeyFile(Path).Load().ShouldBeNull();
    }
}
