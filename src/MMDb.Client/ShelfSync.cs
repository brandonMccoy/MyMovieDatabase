using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MMDb.Application.Vault;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Phone;
using MMDb.Contracts.Sync;
using MMDb.Contracts.Wishlist;
using MMDb.Infrastructure.Persistence;
using MMDb.Local;

namespace MMDb.Client;

/// <summary>
/// Keeps the phone's copy of the collection in step with the PC, which stays the one true copy: what was saved
/// on the phone is asked of the PC again, in order, and then the PC's rows replace the phone's.
/// </summary>
public sealed class ShelfSync(
    PcLink pc,
    HttpCollectionApi remote,
    InProcessCollectionApi local,
    CollectionCopy copy,
    Outbox outbox,
    CoverShelf covers,
    ISettingStore settings,
    TimeProvider clock) : IPhoneSync, IDisposable
{
    private const string SyncedAtKey = "phone.synced-at";

    // Films carry their whole credits (some 10 KB each), so a request stays around a megabyte.
    private const int RowsPerRequest = 100;

    // After the network changes: Android reports Wi-Fi a little before the PC can be reached over it.
    private static readonly TimeSpan[] SettlesWithin = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    private readonly SemaphoreSlim _writing = new(1, 1);
    private CancellationTokenSource? _settling;
    private readonly Lock _lock = new();
    private Task? _running;
    private bool _again;
    private Task _fetchingCovers = Task.CompletedTask;
    private PhoneSyncState _state;
    private DateTimeOffset? _syncedAt;

    public event Action? Changed;

    public PhoneSyncStatus Status => new(_state, _syncedAt, outbox.Changes.Count, outbox.TurnedDown);

    /// <summary>The covers still being fetched after the last sync; done when the phone has them all.</summary>
    public Task Covers => _fetchingCovers;

    /// <summary>Reads what the phone knew when the app was last closed. Call once, before the first screen.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await outbox.LoadAsync(cancellationToken);
        _syncedAt = DateTimeOffset.TryParse(await settings.GetAsync(SyncedAtKey, cancellationToken), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
    }

    public bool IsUnsent(Guid id) => outbox.Creator(id) is not null;

    public async Task DismissAsync(CancellationToken cancellationToken = default)
    {
        await outbox.DismissAsync(cancellationToken);
        Changed?.Invoke();
    }

    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            // One at a time. Asked for while one runs, it runs once more: something may have changed since it looked.
            if (_running is null)
            {
                _running = RunAsync();
            }
            else
            {
                _again = true;
            }

            return _running.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The phone's network changed (Wi-Fi came back, perhaps the one at home): syncs, and because a network
    /// takes a moment to settle, tries a few more times while the PC still isn't there. A newer change takes
    /// over from one still trying.
    /// </summary>
    public async Task NetworkChangedAsync(CancellationToken cancellationToken = default)
    {
        var mine = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lock)
        {
            _settling?.Cancel();
            _settling = mine;
        }

        try
        {
            foreach (var wait in SettlesWithin)
            {
                await Task.Delay(wait, clock, mine.Token);
                await SyncAsync(mine.Token);
                if (pc.State == PcLinkState.Connected)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The network changed again, or the app is closing.
        }
        finally
        {
            lock (_lock)
            {
                if (_settling == mine)
                {
                    _settling = null;
                }

                mine.Dispose();
            }
        }
    }

    /// <summary>
    /// Makes a change to the phone's copy while nothing else writes to it, then sends it on its way to the PC
    /// without waiting for that.
    /// </summary>
    public async Task<T> ChangeAsync<T>(Func<Task<T>> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        T result;
        await _writing.WaitAsync(cancellationToken);
        try
        {
            result = await change();
        }
        finally
        {
            _writing.Release();
        }

        Changed?.Invoke();
        if (outbox.Changes.Count > 0)
        {
            _ = SyncAsync(CancellationToken.None);
        }

        return result;
    }

    public void Dispose() => _writing.Dispose();

    private async Task RunAsync()
    {
        // Never on the caller's thread: a screen asks and carries on.
        await Task.Yield();
        while (true)
        {
            try
            {
                await OnceAsync();
            }
            catch (Exception e) when (e is PcUnavailableException or HttpRequestException or JsonException or IOException or InvalidOperationException)
            {
                // Away, or the PC answered something unexpected: the copy stays as it is until the next try.
                Set(_state == PhoneSyncState.WrongVersion ? PhoneSyncState.WrongVersion : PhoneSyncState.Idle);
            }

            lock (_lock)
            {
                if (!_again)
                {
                    _running = null;
                    return;
                }

                _again = false;
            }
        }
    }

    private async Task OnceAsync()
    {
        if (pc.State is PcLinkState.NotPaired or PcLinkState.Unpaired)
        {
            return;
        }

        // Finds out whether the PC is there before anything is held up waiting for it.
        await pc.RefreshKeysAsync();
        Set(PhoneSyncState.Syncing);

        bool complete;
        await _writing.WaitAsync();
        var holding = true;
        try
        {
            // With something sent, the copy is behind the PC until the pull is through, so nothing else
            // changes it meanwhile. With nothing sent (the first, long pull), saves go on between requests.
            var sent = await PushAsync();
            if (!sent)
            {
                _writing.Release();
                holding = false;
            }

            complete = await PullAsync(holdingTheCopy: sent);
        }
        finally
        {
            if (holding)
            {
                _writing.Release();
            }
        }

        if (complete)
        {
            _syncedAt = clock.GetUtcNow();
            await settings.SetAsync(SyncedAtKey, _syncedAt.Value.ToString("O", CultureInfo.InvariantCulture));
            FetchCovers();
        }

        if (_state == PhoneSyncState.Syncing)
        {
            Set(PhoneSyncState.Idle);
        }
    }

    /// <returns>Whether anything was sent.</returns>
    private async Task<bool> PushAsync()
    {
        var sent = false;
        foreach (var change in outbox.Changes)
        {
            if (!await SendAsync(change))
            {
                await outbox.TurnedDownAsync(change.What);
            }

            await outbox.RemoveAsync(change.Id);
            sent = true;
            Changed?.Invoke();
        }

        return sent;
    }

    /// <returns>
    /// <see langword="false"/> if the PC wouldn't take it. A change that leaves the PC as it is (it has the
    /// format or the copy already, the wish is gone already) counts as taken.
    /// </returns>
    private async Task<bool> SendAsync(PendingChange change)
    {
        try
        {
            return change.Kind switch
            {
                PendingKind.AddMovie => Taken((await remote.AddMovieAsync(change.Read<SaveMovieRequest>())).Status),
                PendingKind.AddComic => Taken((await remote.AddComicAsync(change.Read<SaveComicRequest>())).Status),
                PendingKind.WantMovie => Taken((await remote.WantMovieAsync(change.Read<WantMovieRequest>())).Status),
                PendingKind.WantComic => Taken((await remote.WantComicAsync(change.Read<WantComicRequest>())).Status),
                PendingKind.WantMissing => Taken((await remote.WantMissingIssuesAsync(change.Read<WantMissingRequest>())).Status),
                PendingKind.ChangeWishFormat =>
                    (await remote.ChangeWishlistFormatAsync(change.Subject, change.Read<ChangeWishFormatRequest>().Format)).Status is SaveStatus.Saved or SaveStatus.NotFound or SaveStatus.AlreadyOwned,
                PendingKind.RemoveWishMovie => await remote.RemoveWishlistMovieAsync(change.Subject) || true,
                PendingKind.RemoveWishComic => await remote.RemoveWishlistComicAsync(change.Subject) || true,
                _ => false,
            };
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            return false;
        }

        static bool Taken(SaveStatus status) => status is SaveStatus.Saved or SaveStatus.AlreadyOwned;
    }

    /// <returns><see langword="false"/> if it stopped short: the versions differ, or something was saved on the phone meanwhile.</returns>
    private async Task<bool> PullAsync(bool holdingTheCopy)
    {
        var manifest = await GetAsync<SyncManifest>(HttpMethod.Get, "sync", body: null);
        if (manifest.Schema != await copy.SchemaAsync())
        {
            Set(PhoneSyncState.WrongVersion);
            return false;
        }

        var mine = (await copy.ListAsync()).ToDictionary(t => t.Name, t => t.Stamps);
        var changed = false;
        foreach (var name in CollectionCopy.Tables)
        {
            if (manifest.Tables.FirstOrDefault(t => t.Name == name) is not { } theirs)
            {
                continue;
            }

            var have = mine.GetValueOrDefault(name) ?? new Dictionary<string, string>();
            var gone = have.Keys.Where(key => !theirs.Rows.ContainsKey(key)).ToList();
            if (gone.Count > 0)
            {
                if (!await WriteAsync(() => copy.RemoveAsync(name, gone), holdingTheCopy))
                {
                    return false;
                }

                changed = true;
            }

            foreach (var keys in theirs.Rows.Where(row => have.GetValueOrDefault(row.Key) != row.Value).Select(row => row.Key).Chunk(RowsPerRequest))
            {
                var rows = (await GetAsync<SyncRowsResponse>(HttpMethod.Post, "sync/rows", new SyncRowsRequest(name, keys))).Rows;
                if (!await WriteAsync(() => copy.WriteAsync(name, [.. rows.Select(r => new CopiedRow(r.Key, Plain(r.Values), [.. r.Parts.Select(Plain)]))]), holdingTheCopy))
                {
                    return false;
                }

                changed = true;
                local.RaiseChanged();
            }
        }

        if (changed)
        {
            local.RaiseChanged();
        }

        return true;
    }

    // Something saved on the phone meanwhile goes to the PC first; the next round then pulls.
    private async Task<bool> WriteAsync(Func<Task> write, bool holdingTheCopy)
    {
        if (holdingTheCopy)
        {
            await write();
            return true;
        }

        await _writing.WaitAsync();
        try
        {
            if (outbox.Changes.Count > 0)
            {
                lock (_lock)
                {
                    _again = true;
                }

                return false;
            }

            await write();
            return true;
        }
        finally
        {
            _writing.Release();
        }
    }

    private void FetchCovers()
    {
        lock (_lock)
        {
            if (_fetchingCovers.IsCompleted)
            {
                _fetchingCovers = Task.Run(async () =>
                {
                    // Screens don't wait for this: a cover not here yet is fetched when a page shows it.
                    try
                    {
                        await covers.KeepAsync(await copy.CoverNamesAsync());
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        // The next sync tries again.
                    }
                });
            }
        }
    }

    private async Task<T> GetAsync<T>(HttpMethod method, string path, object? body)
    {
        using var content = body is null ? null : JsonContent.Create(body, body.GetType(), options: ShelfJson.Options);
        using var response = await pc.SendAsync(method, path, content);
        return response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<T>(ShelfJson.Options) is { } value
            ? value
            : throw new HttpRequestException($"The PC answered {(int)response.StatusCode} to {method} {path}.", null, response.StatusCode);
    }

    // JSON has no types to speak of: a column arrives as text, a number or nothing, which is all SQLite holds.
    private static Dictionary<string, object?> Plain(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(v => v.Key, v => v.Value is JsonElement json
            ? json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number => json.TryGetInt64(out var whole) ? whole : json.GetDouble(),
                JsonValueKind.True => 1L,
                JsonValueKind.False => 0L,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => throw new JsonException($"The PC sent a {json.ValueKind} for {v.Key}."),
            }
            : v.Value);

    private void Set(PhoneSyncState state)
    {
        _state = state;
        Changed?.Invoke();
    }
}
