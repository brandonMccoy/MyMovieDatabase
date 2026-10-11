using System.Text.Json;
using MMDb.Application.Vault;
using MMDb.Contracts;

namespace MMDb.Client;

/// <summary>The scan queue in the phone's database, beside the changes waiting to be sent.</summary>
public sealed class ScanQueueStore(ISettingStore settings) : IScanQueueStore, IDisposable
{
    private const string Key = "phone.scans";

    // The queue is saved as a whole after every scan; the last one written must be the last one made.
    private readonly SemaphoreSlim _writing = new(1, 1);
    private IReadOnlyList<QueuedScan> _latest = [];

    public async Task<IReadOnlyList<QueuedScan>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await settings.GetAsync(Key, cancellationToken) is { } json
                ? JsonSerializer.Deserialize<List<QueuedScan>>(json, ShelfJson.Options) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<QueuedScan> scans, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scans);
        _latest = scans;
        await _writing.WaitAsync(cancellationToken);
        try
        {
            var latest = _latest;
            await settings.SetAsync(Key, latest.Count == 0 ? null : JsonSerializer.Serialize(latest, ShelfJson.Options), cancellationToken);
        }
        finally
        {
            _writing.Release();
        }
    }

    public void Dispose() => _writing.Dispose();
}
