using MMDb.Application.Devices;
using MMDb.Domain.Devices;

namespace MMDb.Server.Phone;

/// <summary>Lets phones in: pairing with the code, and checking the token on every request. One DI scope per call.</summary>
public sealed class DeviceGate(IServiceScopeFactory scopes)
{
    /// <returns><see langword="null"/> if the code is wrong, used or expired.</returns>
    public async Task<NewPairing?> PairAsync(string? code, string? deviceName, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeviceAccess>().PairAsync(code, deviceName, cancellationToken);
    }

    /// <returns>The phone the token belongs to; <see langword="null"/> if it isn't paired (any more).</returns>
    public async Task<PairedDevice?> AuthenticateAsync(string? token, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeviceAccess>().AuthenticateAsync(token, cancellationToken);
    }
}
