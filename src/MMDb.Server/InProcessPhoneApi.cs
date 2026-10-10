using MMDb.Application.Devices;
using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Server.Phone;
using QRCoder;

namespace MMDb.Server;

/// <summary><see cref="IPhoneApi"/> for the PC app's Settings screen.</summary>
public sealed class InProcessPhoneApi : IPhoneApi, IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PairingDesk _desk;
    private readonly PhoneServer _server;

    public InProcessPhoneApi(IServiceScopeFactory scopes, PairingDesk desk, PhoneServer server)
    {
        _scopes = scopes;
        _desk = desk;
        _server = server;
        _desk.Changed += OnChanged;
        _server.Changed += OnChanged;
    }

    public event Action? Changed;

    public PhoneServerDto Server => _server.Status;

    public PairingCodeDto PairingCode()
    {
        var code = _desk.Current();
        var status = _server.Status;
        if (status.State != PhoneServerState.Listening || _server.Fingerprint is not { } fingerprint)
        {
            return new(code.Value, code.ExpiresAt, []);
        }

        var link = new PairingLink(status.Addresses, status.Port, code.Value, fingerprint, _server.PcName);
        using var qr = QRCodeGenerator.GenerateQrCode(link.ToString(), QRCodeGenerator.ECCLevel.M);
        var rows = qr.ModuleMatrix.Select(row => string.Concat(row.Cast<bool>().Select(dark => dark ? '1' : '0'))).ToList();
        return new(code.Value, code.ExpiresAt, rows);
    }

    public Task<IReadOnlyList<PairedDeviceDto>> ListDevicesAsync(CancellationToken cancellationToken = default) =>
        With<IReadOnlyList<PairedDeviceDto>>(async devices =>
            [.. (await devices.ListAsync(cancellationToken)).Select(d => new PairedDeviceDto(d.Id, d.Name, d.PairedAt, d.LastSeenAt))]);

    public Task<bool> UnpairAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
        With(devices => devices.UnpairAsync(deviceId, cancellationToken));

    public void Dispose()
    {
        _desk.Changed -= OnChanged;
        _server.Changed -= OnChanged;
    }

    private void OnChanged() => Changed?.Invoke();

    private async Task<T> With<T>(Func<DeviceAccess, Task<T>> use)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<DeviceAccess>());
    }
}
