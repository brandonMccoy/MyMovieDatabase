namespace MMDb.Application.Metadata;

public enum KeyCheck
{
    Accepted,

    /// <summary>The service turned the key down.</summary>
    Rejected,

    /// <summary>Offline, timed out, or the service failed: nothing is known about the key.</summary>
    Unknown,
}

/// <summary>Port: asks an online database whether it takes a key, without looking anything up.</summary>
public interface IKeyChecker
{
    Task<KeyCheck> CheckTmdbAsync(string key, CancellationToken cancellationToken = default);

    Task<KeyCheck> CheckMetronAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>The keys set in Settings; <see langword="null"/> for a service without one.</summary>
public sealed record SavedKeys(string? Tmdb = null, string? Metron = null);

/// <summary>Port: where the PC keeps <see cref="SavedKeys"/> between runs.</summary>
public interface IKeyStore
{
    /// <returns><see langword="null"/> if nothing has been saved yet, or it can't be read any more.</returns>
    SavedKeys? Load();

    void Save(SavedKeys keys);
}
