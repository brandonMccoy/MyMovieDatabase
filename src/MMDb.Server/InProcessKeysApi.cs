using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Keys;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Server;

/// <summary>
/// <see cref="IKeysApi"/> for the PC app. The keys in use are the ones in <see cref="TmdbSettings"/> and
/// <see cref="MetronSettings"/>, which the lookups and <c>GET /api/v1/keys</c> read each time: a change here
/// takes effect at once, and reaches a paired phone on its next sync.
/// </summary>
public sealed class InProcessKeysApi(IKeyStore store, IKeyChecker checker, TmdbSettings tmdb, MetronSettings metron) : IKeysApi
{
    private static readonly OnlineService[] Services = [OnlineService.Tmdb, OnlineService.Metron];

    private readonly Lock _gate = new();

    // What each service said about the key it has now. Nothing yet: not checked.
    private readonly Dictionary<OnlineService, KeyState> _states = [];

    public event Action? Changed;

    public IReadOnlyList<OnlineKeyDto> Keys
    {
        get
        {
            lock (_gate)
            {
                return [.. Services.Select(service => KeyOf(service) is { } key
                    ? new OnlineKeyDto(service, _states.GetValueOrDefault(service, KeyState.NotChecked), key.Length > 8 ? key[^4..] : null)
                    : new OnlineKeyDto(service, KeyState.NotSet))];
            }
        }
    }

    // Until something is saved in Settings, the keys the app was started with (a developer's user secrets) apply.
    public void Load()
    {
        if (store.Load() is { } saved)
        {
            lock (_gate)
            {
                tmdb.ApiKey = saved.Tmdb;
                metron.Token = saved.Metron;
            }
        }
    }

    public Task CheckAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(Services.Select(service => CheckAsync(service, cancellationToken)));

    private async Task CheckAsync(OnlineService service, CancellationToken cancellationToken)
    {
        string? key;
        lock (_gate)
        {
            key = KeyOf(service);

            // An answer already on screen stays there until the new one is in.
            if (key is not null && !_states.ContainsKey(service))
            {
                _states[service] = KeyState.Checking;
            }
        }

        if (key is null)
        {
            return;
        }

        Changed?.Invoke();
        var state = KeyState.NotChecked;
        try
        {
            state = ToState(await AskAsync(service, key, cancellationToken));
        }
        finally
        {
            lock (_gate)
            {
                // Unless the key was changed or removed meanwhile.
                if (KeyOf(service) == key)
                {
                    _states[service] = state;
                }
            }

            Changed?.Invoke();
        }
    }

    public async Task<bool> SaveAsync(OnlineService service, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        key = key.Trim();
        var check = await AskAsync(service, key, cancellationToken);
        if (check == KeyCheck.Rejected)
        {
            return false;
        }

        Keep(service, key, ToState(check));
        return true;
    }

    public void Remove(OnlineService service) => Keep(service, null, KeyState.NotSet);

    /// <param name="key"><see langword="null"/>: the service has no key from now on.</param>
    private void Keep(OnlineService service, string? key, KeyState state)
    {
        lock (_gate)
        {
            if (service == OnlineService.Tmdb)
            {
                tmdb.ApiKey = key;
            }
            else
            {
                metron.Token = key;
            }

            if (key is null)
            {
                _states.Remove(service);
            }
            else
            {
                _states[service] = state;
            }

            store.Save(new(KeyOf(OnlineService.Tmdb), KeyOf(OnlineService.Metron)));
        }

        Changed?.Invoke();
    }

    private string? KeyOf(OnlineService service) =>
        (service == OnlineService.Tmdb ? tmdb.ApiKey : metron.Token) is { } key && !string.IsNullOrWhiteSpace(key) ? key.Trim() : null;

    private Task<KeyCheck> AskAsync(OnlineService service, string key, CancellationToken cancellationToken) =>
        service == OnlineService.Tmdb ? checker.CheckTmdbAsync(key, cancellationToken) : checker.CheckMetronAsync(key, cancellationToken);

    private static KeyState ToState(KeyCheck check) => check switch
    {
        KeyCheck.Accepted => KeyState.Connected,
        KeyCheck.Rejected => KeyState.Rejected,
        _ => KeyState.NotChecked,
    };
}
