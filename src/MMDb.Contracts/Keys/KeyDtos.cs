namespace MMDb.Contracts.Keys;

/// <summary>The online databases that need a key.</summary>
public enum OnlineService
{
    Tmdb,
    Metron,
}

public enum KeyState
{
    NotSet,

    /// <summary>The service is being asked about the key right now.</summary>
    Checking,

    Connected,

    /// <summary>The service turned the key down.</summary>
    Rejected,

    /// <summary>The service couldn't be reached, so nothing is known about the key.</summary>
    NotChecked,
}

/// <param name="Ending">The key's last four characters, to tell one key from another. The key itself never reaches the screen.</param>
public sealed record OnlineKeyDto(OnlineService Service, KeyState State, string? Ending = null);
