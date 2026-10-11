using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Server.Http;

namespace MMDb.Server.Phone;

/// <summary>
/// The web server phones talk to, run inside the PC app: HTTPS on the home network with the PC's own
/// certificate. It serves the same <see cref="ICollectionApi"/> the PC's screens use.
/// </summary>
public sealed class PhoneHost(IServiceProvider shelf) : IAsyncDisposable
{
    public const int DefaultPort = 5243;

    private WebApplication? _app;

    /// <summary>Starts listening. A port that can't be opened is reported through <see cref="PhoneServer"/>, not thrown.</summary>
    /// <param name="certificatePath">See <see cref="PhoneCertificate.LoadOrCreate"/>.</param>
    public async Task StartAsync(string certificatePath, int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        var server = shelf.GetRequiredService<PhoneServer>();
        try
        {
            var certificate = PhoneCertificate.LoadOrCreate(certificatePath, shelf.GetRequiredService<TimeProvider>().GetUtcNow());
            var builder = CreateBuilder(shelf);
            builder.WebHost.UseKestrelHttpsConfiguration();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(port, listen => listen.UseHttps(certificate)));
            _app = Build(builder);
            await _app.StartAsync(cancellationToken);

            // Port 0 asks for any free port; the server knows which one it got.
            var bound = _app.Urls.Select(url => new Uri(url).Port).FirstOrDefault(port);
            server.Listening(bound, LocalAddresses(), PhoneCertificate.Fingerprint(certificate));
        }
        catch (Exception e) when (e is IOException or SocketException or CryptographicException or UnauthorizedAccessException)
        {
            server.Failed(port, e.Message);
        }
    }

    /// <summary>A web app that serves <paramref name="shelf"/>'s collection; the caller chooses what it listens on.</summary>
    public static WebApplicationBuilder CreateBuilder(IServiceProvider shelf)
    {
        ArgumentNullException.ThrowIfNull(shelf);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services
            .AddSingleton(shelf.GetRequiredService<ICollectionApi>())
            .AddSingleton(shelf.GetRequiredService<DeviceGate>())
            .AddSingleton(shelf.GetRequiredService<PhoneServer>())
            .AddSingleton(shelf.GetRequiredService<ICoverStore>())
            .AddSingleton(shelf.GetRequiredService<TimeProvider>())
            .AddSingleton(shelf.GetRequiredService<TmdbSettings>())
            .AddSingleton(shelf.GetRequiredService<MetronSettings>())
            .AddSingleton(shelf.GetRequiredService<CollectionCopy>())
            .AddShelfApi();
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapShelfApi();
        return app;
    }

    /// <summary>This PC's IPv4 addresses, the one a phone on the home network most likely reaches first.</summary>
    public static IReadOnlyList<string> LocalAddresses() => OrderAddresses(
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n =>
            {
                var properties = n.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                return properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => (n.NetworkInterfaceType, hasGateway, a.Address.ToString()));
            }));

    /// <summary>
    /// Wi-Fi and wired adapters come before tunnels and virtual ones (a VPN's address is no use to a phone in
    /// the same room), and among those the ones with a gateway first. Loopback and self-assigned addresses are left out.
    /// </summary>
    public static IReadOnlyList<string> OrderAddresses(IEnumerable<(NetworkInterfaceType Type, bool HasGateway, string Address)> adapters) =>
        [.. adapters
            .Where(a => a.Type != NetworkInterfaceType.Loopback
                && !a.Address.StartsWith("127.", StringComparison.Ordinal)
                && !a.Address.StartsWith("169.254.", StringComparison.Ordinal))
            .OrderBy(a => a.Type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx ? 0 : 1)
            .ThenBy(a => a.HasGateway ? 0 : 1)
            .Select(a => a.Address)
            .Distinct()];

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
            shelf.GetRequiredService<PhoneServer>().Stopped();
        }
    }
}
