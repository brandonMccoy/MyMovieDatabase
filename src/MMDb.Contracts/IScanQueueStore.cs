using MMDb.Contracts.Scans;

namespace MMDb.Contracts;

/// <summary>A barcode scanned and not reviewed yet.</summary>
/// <param name="Kind">What the user said it is; <see langword="null"/> left it to the code.</param>
public sealed record QueuedScan(string Code, ProductKind? Kind = null);

/// <summary>Port: where the phone keeps its scan queue between runs. Phone only.</summary>
public interface IScanQueueStore
{
    Task<IReadOnlyList<QueuedScan>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<QueuedScan> scans, CancellationToken cancellationToken = default);
}
