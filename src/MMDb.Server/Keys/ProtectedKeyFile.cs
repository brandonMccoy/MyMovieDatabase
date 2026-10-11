using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using MMDb.Application.Metadata;

namespace MMDb.Server.Keys;

/// <summary>
/// <see cref="IKeyStore"/> in one file beside the database, encrypted by Windows for the signed-in account
/// (DPAPI): another account or another PC can't read it, and the keys stay out of the database and its backups.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedKeyFile(string path) : IKeyStore
{
    public SavedKeys? Load()
    {
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SavedKeys>(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Not there yet, or written by another Windows account: as if nothing was saved.
            return null;
        }
    }

    public void Save(SavedKeys keys)
    {
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(keys), optionalEntropy: null, DataProtectionScope.CurrentUser);

        // Written beside the file and moved over it, so a crash halfway leaves the keys saved before.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
