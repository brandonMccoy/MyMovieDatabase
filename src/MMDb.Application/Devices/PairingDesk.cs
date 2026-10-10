using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MMDb.Application.Devices;

/// <param name="Value">Six digits.</param>
public sealed record PairingCode(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Hands out the one-time code a phone pairs with. A code exists only after the Settings screen asked for one,
/// works once and for five minutes; a few wrong guesses throw it away and close pairing for a minute, so
/// guessing all million codes isn't practical.
/// </summary>
public sealed class PairingDesk(TimeProvider clock)
{
    public const int MaxWrongGuesses = 5;

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LockoutAfterGuessing = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private PairingCode? _code;
    private int _wrongGuesses;
    private DateTimeOffset _closedUntil;

    /// <summary>The code changed, or a phone paired, unpaired or checked in.</summary>
    public event Action? Changed;

    /// <summary>The code to show now: the current one, or a new one if it expired or was used.</summary>
    public PairingCode Current()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_code is null || _code.ExpiresAt <= now)
            {
                _code = new PairingCode(
                    RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture),
                    now + CodeLifetime);
                _wrongGuesses = 0;
            }

            return _code;
        }
    }

    /// <summary>Uses the code up if <paramref name="code"/> is it (spaces are ignored).</summary>
    public bool TryRedeem(string? code)
    {
        bool redeemed;
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_code is null || _code.ExpiresAt <= now || now < _closedUntil)
            {
                return false;
            }

            var guess = Encoding.UTF8.GetBytes((code ?? "").Replace(" ", "", StringComparison.Ordinal));
            redeemed = CryptographicOperations.FixedTimeEquals(guess, Encoding.UTF8.GetBytes(_code.Value));
            if (redeemed)
            {
                _code = null;
            }
            else if (++_wrongGuesses >= MaxWrongGuesses)
            {
                _code = null;
                _closedUntil = now + LockoutAfterGuessing;
            }
            else
            {
                return false;
            }
        }

        Changed?.Invoke();
        return redeemed;
    }

    internal void NotifyChanged() => Changed?.Invoke();
}
