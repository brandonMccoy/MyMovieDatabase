namespace MMDb.Contracts.Phone;

/// <summary>Sent by a phone to pair with the PC.</summary>
/// <param name="Code">The six digits the PC shows in Settings.</param>
/// <param name="DeviceName">What the phone calls itself, e.g. "Pixel 8".</param>
public sealed record PairRequest(string Code, string DeviceName);

/// <param name="Token">Proves it is this phone from now on. Sent once; the PC keeps only a hash.</param>
/// <param name="PcName">The PC's name, to show on the phone.</param>
public sealed record PairResponse(string Token, Guid DeviceId, string PcName);

/// <summary>Answer to a paired phone checking that it can still reach the PC.</summary>
public sealed record SessionResponse(string PcName, string DeviceName);

/// <summary>
/// The PC's keys for the online databases, handed to a paired phone so it can look things up by itself.
/// <see langword="null"/> for one the PC has no key for.
/// </summary>
public sealed record LookupKeysResponse(string? TmdbApiKey, string? MetronToken);

public enum PhoneServerState
{
    /// <summary>Not started (yet).</summary>
    Off,

    /// <summary>Phones on the same network can reach the PC.</summary>
    Listening,

    /// <summary>The port could not be opened; see <see cref="PhoneServerDto.Problem"/>.</summary>
    Failed,
}

/// <summary>How phones reach this PC.</summary>
/// <param name="Addresses">The PC's addresses on the local network, most likely first.</param>
public sealed record PhoneServerDto(PhoneServerState State, int Port, IReadOnlyList<string> Addresses, string? Problem = null);

/// <summary>The one-time code a phone pairs with, also as a QR code.</summary>
/// <param name="Code">Six digits.</param>
/// <param name="QrRows">The QR code, one string per row: <c>1</c> is a dark module, <c>0</c> a light one.</param>
public sealed record PairingCodeDto(string Code, DateTimeOffset ExpiresAt, IReadOnlyList<string> QrRows);

/// <param name="LastSeenAt">When the phone last talked to the PC.</param>
public sealed record PairedDeviceDto(Guid Id, string Name, DateTimeOffset PairedAt, DateTimeOffset? LastSeenAt);

/// <summary>Where a phone stands with its PC.</summary>
public enum PcLinkState
{
    /// <summary>Never paired, or forgotten.</summary>
    NotPaired,

    /// <summary>Paired, not checked yet.</summary>
    Unknown,

    Connected,

    /// <summary>The PC didn't answer: it is off, asleep, or the phone is on another network.</summary>
    Unreachable,

    /// <summary>The PC answered but no longer knows this phone (it was unpaired there).</summary>
    Unpaired,
}

public enum PairStatus
{
    Paired,

    /// <summary>The text isn't a Shelf pairing code, or the address or code is missing.</summary>
    NotACode,

    /// <summary>No PC answered at that address.</summary>
    Unreachable,

    /// <summary>The PC turned the code down: mistyped, used already, or expired.</summary>
    WrongCode,

    /// <summary>The PC that answered isn't the one the QR code came from.</summary>
    WrongPc,
}
