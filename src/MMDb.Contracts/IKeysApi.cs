using MMDb.Contracts.Keys;

namespace MMDb.Contracts;

/// <summary>The PC's keys for the online databases, set in Settings. PC only: a paired phone is handed its PC's keys.</summary>
public interface IKeysApi
{
    event Action? Changed;

    /// <summary>One for each service, TMDB first.</summary>
    IReadOnlyList<OnlineKeyDto> Keys { get; }

    /// <summary>Picks up the keys saved last time. Called once at startup, before anything is looked up.</summary>
    void Load();

    /// <summary>Asks each service about its key. Called when Settings opens.</summary>
    Task CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the service about the key, then keeps it. A key that couldn't be checked (offline) is kept too.</summary>
    /// <returns><see langword="false"/> if the service turned the key down; the key before it stays.</returns>
    Task<bool> SaveAsync(OnlineService service, string key, CancellationToken cancellationToken = default);

    void Remove(OnlineService service);
}
