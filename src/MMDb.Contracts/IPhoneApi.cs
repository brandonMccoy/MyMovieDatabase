using MMDb.Contracts.Phone;

namespace MMDb.Contracts;

/// <summary>Pairing phones with this PC, for the Settings screen. PC only.</summary>
public interface IPhoneApi
{
    /// <summary>The server started or stopped, the pairing code changed, or a phone paired, unpaired or checked in.</summary>
    event Action? Changed;

    PhoneServerDto Server { get; }

    /// <summary>The code to pair with right now; a new one once it has expired or been used.</summary>
    PairingCodeDto PairingCode();

    /// <summary>Newest pairing first.</summary>
    Task<IReadOnlyList<PairedDeviceDto>> ListDevicesAsync(CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> if there was no such phone.</returns>
    Task<bool> UnpairAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
