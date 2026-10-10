using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Server;
using MMDb.Server.Phone;

namespace MMDb.Testing;

/// <summary>
/// The PC as a phone sees it: the real collection (in-memory SQLite, fake online databases) behind the real
/// web API, served in memory instead of on the network.
/// </summary>
internal sealed class TestPc : IAsyncDisposable
{
    public const string Name = "DESK";
    public const string Address = "192.168.1.24";
    public const string Fingerprint = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    public static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    public static readonly Uri CoverBase = new("https://covers.shelf.example/");

    private readonly InMemorySqlite _sqlite = new();
    private readonly ServiceProvider _services;
    private WebApplication _app = null!;

    private TestPc()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), CoverBase)
            .AddSingleton<TimeProvider>(Clock)
            .Replace(ServiceDescriptor.Singleton(new PhoneServer { PcName = Name }));
        _services = Online.ReplaceIn(services).BuildServiceProvider();
    }

    public FakeTimeProvider Clock { get; } = new(Start);

    public FakeOnline Online { get; } = new();

    /// <summary>The collection as the PC's own screens use it.</summary>
    public ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    public IPhoneApi Phone => _services.GetRequiredService<IPhoneApi>();

    public PhoneServer Server => _services.GetRequiredService<PhoneServer>();

    /// <summary>The code the PC's Settings screen shows right now.</summary>
    public string PairingCode => Phone.PairingCode().Code;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<TestPc> StartAsync()
    {
        var pc = new TestPc();
        await pc._services.MigrateShelfDatabaseAsync(Ct);
        var builder = PhoneHost.CreateBuilder(pc._services);
        builder.WebHost.UseTestServer();
        pc._app = PhoneHost.Build(builder);
        await pc._app.StartAsync(Ct);
        pc.Server.Listening(PhoneHost.DefaultPort, [Address], Fingerprint);
        return pc;
    }

    /// <summary>Serves this PC over real HTTPS on this machine, on a free port (see <see cref="Server"/> for which).</summary>
    /// <param name="certificatePath">Where the PC keeps its certificate.</param>
    public async Task<PhoneHost> ListenAsync(string certificatePath, int port = 0)
    {
        var host = new PhoneHost(_services);
        await host.StartAsync(certificatePath, port, Ct);
        return host;
    }

    /// <summary>A client with no token: what any device on the network can do.</summary>
    public HttpClient CreateClient() => _app.GetTestClient();

    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    /// <summary>Pairs a phone the way <c>POST /api/v1/pair</c> does, and returns its token.</summary>
    public async Task<string> PairAsync(string deviceName = "Pixel 8")
    {
        var pairing = await _services.GetRequiredService<DeviceGate>().PairAsync(PairingCode, deviceName, Ct);
        return pairing.ShouldNotBeNull().Token;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }
}
