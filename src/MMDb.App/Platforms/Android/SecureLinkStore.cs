using System.Text.Json;
using MMDb.Client;

namespace MMDb.App;

/// <summary>Keeps the phone's pairing (the PC's address, certificate fingerprint and this phone's token) in Android's encrypted storage.</summary>
internal sealed class SecureLinkStore : ILinkStore
{
    private const string Key = "shelf.pc-link";

    public async Task<PcLinkRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key) is { Length: > 0 } json
                ? JsonSerializer.Deserialize<PcLinkRecord>(json)
                : null;
        }
        catch (Exception e) when (e is JsonException or Java.Lang.Exception)
        {
            // Unreadable (restored onto another phone, or damaged): pair again.
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public Task SaveAsync(PcLinkRecord record, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(record));

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
