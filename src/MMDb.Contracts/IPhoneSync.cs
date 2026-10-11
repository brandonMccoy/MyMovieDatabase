namespace MMDb.Contracts;

public enum PhoneSyncState
{
    /// <summary>Nothing going on. Whether the PC is in reach is <see cref="IPcLink.State"/>.</summary>
    Idle,

    Syncing,

    /// <summary>Shelf on the phone and on the PC keep the collection differently: one of them needs updating.</summary>
    WrongVersion,
}

/// <param name="SyncedAt">When the copy was last brought up to date; <see langword="null"/> if it never was.</param>
/// <param name="Waiting">Changes made on the phone that the PC hasn't been sent.</param>
/// <param name="TurnedDown">Changes the PC wouldn't take, each in a few words, until <see cref="IPhoneSync.DismissAsync"/>.</param>
public sealed record PhoneSyncStatus(PhoneSyncState State, DateTimeOffset? SyncedAt, int Waiting, IReadOnlyList<string> TurnedDown);

/// <summary>
/// The phone's own copy of the PC's collection: how fresh it is, and what was saved on the phone that the PC
/// hasn't got yet. Phone only.
/// </summary>
public interface IPhoneSync
{
    /// <summary>May be raised on any thread.</summary>
    event Action? Changed;

    PhoneSyncStatus Status { get; }

    /// <summary>
    /// Sends the PC what is waiting, then brings the copy up to date. Returns once that is done or the PC
    /// turned out not to be in reach; it doesn't throw for that.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether this film, comic or wishlist item was added on the phone and hasn't been sent yet.</summary>
    bool IsUnsent(Guid id);

    /// <summary>The user has seen <see cref="PhoneSyncStatus.TurnedDown"/>.</summary>
    Task DismissAsync(CancellationToken cancellationToken = default);
}
