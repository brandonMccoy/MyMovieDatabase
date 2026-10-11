using System.Text.Json;
using MMDb.Application.Vault;
using MMDb.Contracts;

namespace MMDb.Client;

public enum PendingKind
{
    AddMovie,
    AddComic,
    WantMovie,
    WantComic,
    WantMissing,
    ChangeWishFormat,
    RemoveWishMovie,
    RemoveWishComic,
}

/// <summary>Something saved on the phone that the PC hasn't been told yet.</summary>
/// <param name="Request">What was asked, as JSON, to be asked of the PC as it was of the phone's copy.</param>
/// <param name="Subject">The film, comic or wishlist item in the phone's copy that it made or changed.</param>
/// <param name="Creates">It made <paramref name="Subject"/>: until this is sent, that exists only on the phone.</param>
/// <param name="What">For the user, should the PC turn it down: "Heat (1995) on Blu-ray".</param>
public sealed record PendingChange(Guid Id, PendingKind Kind, string Request, Guid Subject, bool Creates, string What)
{
    public T Read<T>() => JsonSerializer.Deserialize<T>(Request, ShelfJson.Options) ?? throw new InvalidOperationException($"A pending {Kind} has no request.");
}

/// <summary>
/// The changes waiting to be sent to the PC, oldest first, and what the PC turned down. Kept in the phone's
/// database, so they are still there after the app was closed.
/// </summary>
public sealed class Outbox(ISettingStore settings)
{
    private const string ChangesKey = "phone.outbox";
    private const string TurnedDownKey = "phone.turned-down";

    private readonly Lock _lock = new();
    private List<PendingChange> _changes = [];
    private List<string> _turnedDown = [];

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<PendingChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return [.. _changes];
            }
        }
    }

    /// <summary>What the PC wouldn't take, in the user's words, until they have seen it.</summary>
    public IReadOnlyList<string> TurnedDown
    {
        get
        {
            lock (_lock)
            {
                return [.. _turnedDown];
            }
        }
    }

    /// <summary>The change that made this film, comic or wishlist item, if it hasn't been sent yet.</summary>
    public PendingChange? Creator(Guid subject)
    {
        lock (_lock)
        {
            return _changes.Find(c => c.Creates && c.Subject == subject);
        }
    }

    /// <summary>Reads what was waiting when the app was last closed. Call once, before anything is saved.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var changes = Read<List<PendingChange>>(await settings.GetAsync(ChangesKey, cancellationToken));
        var turnedDown = Read<List<string>>(await settings.GetAsync(TurnedDownKey, cancellationToken));
        lock (_lock)
        {
            _changes = changes ?? [];
            _turnedDown = turnedDown ?? [];
        }
    }

    public Task AddAsync<TRequest>(PendingKind kind, TRequest request, Guid subject, bool creates, string what, CancellationToken cancellationToken = default) =>
        ChangeAsync(changes => changes.Add(new(Guid.NewGuid(), kind, JsonSerializer.Serialize(request, ShelfJson.Options), subject, creates, what)), cancellationToken);

    /// <summary>Asks something else of the PC in a change's place: what it made was changed again before it was sent.</summary>
    public Task RewriteAsync<TRequest>(Guid id, TRequest request, string what, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            changes =>
            {
                if (changes.FindIndex(c => c.Id == id) is var at and >= 0)
                {
                    changes[at] = changes[at] with { Request = JsonSerializer.Serialize(request, ShelfJson.Options), What = what };
                }
            },
            cancellationToken);

    /// <summary>Sent, or not to be sent after all.</summary>
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(changes => changes.RemoveAll(c => c.Id == id), cancellationToken);

    /// <summary>Drops every change to something that only ever existed on the phone.</summary>
    public Task ForgetAsync(Guid subject, CancellationToken cancellationToken = default) =>
        ChangeAsync(changes => changes.RemoveAll(c => c.Subject == subject), cancellationToken);

    public async Task TurnedDownAsync(string what, CancellationToken cancellationToken = default)
    {
        List<string> turnedDown;
        lock (_lock)
        {
            _turnedDown.Add(what);
            turnedDown = [.. _turnedDown];
        }

        await settings.SetAsync(TurnedDownKey, JsonSerializer.Serialize(turnedDown, ShelfJson.Options), cancellationToken);
    }

    /// <summary>The user has seen what was turned down.</summary>
    public async Task DismissAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _turnedDown = [];
        }

        await settings.SetAsync(TurnedDownKey, null, cancellationToken);
    }

    private async Task ChangeAsync(Action<List<PendingChange>> change, CancellationToken cancellationToken)
    {
        List<PendingChange> changes;
        lock (_lock)
        {
            change(_changes);
            changes = [.. _changes];
        }

        await settings.SetAsync(ChangesKey, changes.Count == 0 ? null : JsonSerializer.Serialize(changes, ShelfJson.Options), cancellationToken);
    }

    private static T? Read<T>(string? json)
        where T : class
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize<T>(json, ShelfJson.Options);
        }
        catch (JsonException)
        {
            // Written by a newer Shelf, or damaged: start over rather than not start at all.
            return null;
        }
    }
}
