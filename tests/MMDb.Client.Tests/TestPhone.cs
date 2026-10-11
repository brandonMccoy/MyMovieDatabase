using Microsoft.Extensions.DependencyInjection;
using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>
/// A phone as the app puts it together: its own database (in memory) and online databases (faked), talking to
/// a <see cref="TestPc"/> in memory.
/// </summary>
internal sealed class TestPhone : IDisposable
{
    private readonly InMemorySqlite _sqlite;
    private readonly bool _ownsData;
    private readonly ServiceProvider _services;

    /// <param name="restartOf">The phone whose app this is, closed and opened again: same pairing, database and covers.</param>
    public TestPhone(TestPc pc, TestPhone? restartOf = null)
    {
        Pc = pc;
        Transport = restartOf?.Transport ?? new TestTransport(pc);
        Store = restartOf?.Store ?? new MemoryLinkStore();
        Online = restartOf?.Online ?? new FakeOnline();
        Files = restartOf?.Files ?? new TempFolder();
        _sqlite = restartOf?._sqlite ?? new InMemorySqlite();
        _ownsData = restartOf is null;

        var settings = FakeOnline.Settings(_sqlite);
        var services = new ServiceCollection()
            .AddSingleton<ILinkStore>(Store)
            .AddSingleton<IPcTransport>(Transport)
            .AddSingleton(new PhoneIdentity("Pixel 8"))
            .AddSingleton<TimeProvider>(pc.Clock)
            .AddShelfPhone(new() { ConnectionString = settings.ConnectionString, CoverDirectory = Files["covers"] }, TestPc.CoverBase);
        _services = Online.ReplaceIn(services).BuildServiceProvider();
        Link = _services.GetRequiredService<PcLink>();
        Api = _services.GetRequiredService<HttpCollectionApi>();
    }

    public TestPc Pc { get; }

    public TestTransport Transport { get; }

    public MemoryLinkStore Store { get; }

    /// <summary>The online databases as the phone itself reaches them, away from home too.</summary>
    public FakeOnline Online { get; }

    /// <summary>The app's own files; its covers are under <c>covers</c>.</summary>
    public TempFolder Files { get; }

    public PcLink Link { get; }

    /// <summary>The phone's own database.</summary>
    public string ConnectionString => _sqlite.ConnectionString;

    /// <summary>Straight to the PC over the link, as the phone sends what it saved.</summary>
    public ICollectionApi Api { get; }

    /// <summary>What the phone's screens use: its own copy, with what is saved there sent on to the PC.</summary>
    public ICollectionApi Shelf => _services.GetRequiredService<ICollectionApi>();

    public ShelfSync Sync => _services.GetRequiredService<ShelfSync>();

    public Outbox Outbox => _services.GetRequiredService<Outbox>();

    public CoverShelf Covers => _services.GetRequiredService<CoverShelf>();

    public T Service<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>The PC can't be reached: the phone is away from home, or the PC is off.</summary>
    public bool Away
    {
        get => Transport.Down.Contains(TestPc.Address);
        set => _ = value ? Transport.Down.Add(TestPc.Address) : Transport.Down.Remove(TestPc.Address);
    }

    /// <summary>What the app does before its first screen.</summary>
    public async Task StartAsync()
    {
        await _services.StartShelfPhoneAsync(TestContext.Current.CancellationToken);
        await Link.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The text of the QR code the PC shows right now.</summary>
    public string QrText(params string[] hosts) =>
        new PairingLink(hosts.Length == 0 ? [TestPc.Address] : hosts, 5243, Pc.PairingCode, TestPc.Fingerprint, TestPc.Name).ToString();

    public async Task PairAsync()
    {
        await StartAsync();
        (await Link.PairAsync(QrText(), TestContext.Current.CancellationToken)).ShouldBe(PairStatus.Paired);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (_ownsData)
        {
            _sqlite.Dispose();
            Files.Dispose();
        }
    }
}

/// <summary>Carries requests to the <see cref="TestPc"/>; hosts in <see cref="Down"/> don't answer.</summary>
internal sealed class TestTransport(TestPc pc) : IPcTransport
{
    private readonly List<string> _asked = [];

    public HashSet<string> Down { get; } = [];

    /// <summary>Paths under <c>/api/v1/</c> the PC fails on (a 500), e.g. <c>wishlist/movies</c>.</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>The host and path of every request, in order.</summary>
    public IReadOnlyList<string> Asked { get { lock (_asked) { return [.. _asked]; } } }

    public HttpMessageHandler CreateHandler(CertificatePin pin) => new Gate(this, pc.CreateHandler());

    private sealed class Gate(TestTransport transport, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (transport._asked)
            {
                transport._asked.Add(uri.Host + uri.AbsolutePath);
            }

            return transport.Down.Contains(uri.Host)
                ? throw new HttpRequestException(HttpRequestError.ConnectionError, "No route to host.")
                : transport.Failing.Contains(uri.AbsolutePath["/api/v1/".Length..])
                    ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { RequestMessage = request })
                    : base.SendAsync(request, cancellationToken);
        }
    }
}

internal sealed class MemoryLinkStore : ILinkStore
{
    public PcLinkRecord? Record { get; set; }

    public Task<PcLinkRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Record);

    public Task SaveAsync(PcLinkRecord record, CancellationToken cancellationToken = default)
    {
        Record = record;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Record = null;
        return Task.CompletedTask;
    }
}
