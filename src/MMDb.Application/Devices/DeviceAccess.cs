using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MMDb.Domain.Devices;

namespace MMDb.Application.Devices;

/// <summary>Port: where paired phones are stored. One instance is one unit of work.</summary>
public interface IDeviceRepository
{
    Task<PairedDevice?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PairedDevice?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default);

    void Add(PairedDevice device);

    void Remove(PairedDevice device);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <param name="Token">Given to the phone once; only its hash is kept.</param>
public sealed record NewPairing(string Token, PairedDevice Device);

/// <summary>Pairs phones with the PC, checks the token a phone sends with every request, and unpairs them.</summary>
public sealed class DeviceAccess(IDeviceRepository devices, PairingDesk desk, TimeProvider clock)
{
    /// <returns><see langword="null"/> if the code is wrong, used or expired.</returns>
    public async Task<NewPairing?> PairAsync(string? code, string? deviceName, CancellationToken cancellationToken = default)
    {
        if (!desk.TryRedeem(code))
        {
            return null;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var device = PairedDevice.Create(deviceName, Hash(token), clock.GetUtcNow());
        devices.Add(device);
        await devices.SaveChangesAsync(cancellationToken);
        desk.NotifyChanged();
        return new NewPairing(token, device);
    }

    /// <returns>The phone the token belongs to; <see langword="null"/> if it isn't paired (any more).</returns>
    public async Task<PairedDevice?> AuthenticateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || await devices.FindByTokenHashAsync(Hash(token), cancellationToken) is not { } device)
        {
            return null;
        }

        if (device.Seen(clock.GetUtcNow()))
        {
            await devices.SaveChangesAsync(cancellationToken);
            desk.NotifyChanged();
        }

        return device;
    }

    /// <summary>Newest pairing first.</summary>
    public async Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await devices.ListAsync(cancellationToken)).OrderByDescending(d => d.PairedAt)];

    /// <returns><see langword="false"/> if there was no such phone.</returns>
    public async Task<bool> UnpairAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await devices.FindAsync(id, cancellationToken) is not { } device)
        {
            return false;
        }

        devices.Remove(device);
        await devices.SaveChangesAsync(cancellationToken);
        desk.NotifyChanged();
        return true;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}
