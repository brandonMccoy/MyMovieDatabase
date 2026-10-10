using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>A phone's side of the link, talking to a <see cref="TestPc"/> in memory.</summary>
internal sealed class TestPhone : IDisposable
{
    public TestPhone(TestPc pc)
    {
        Pc = pc;
        Transport = new TestTransport(pc);
        Link = new PcLink(Store, Transport, new PhoneIdentity("Pixel 8"));
        Api = new HttpCollectionApi(Link);
    }

    public TestPc Pc { get; }

    public TestTransport Transport { get; }

    public MemoryLinkStore Store { get; } = new();

    public PcLink Link { get; }

    public ICollectionApi Api { get; }

    /// <summary>The text of the QR code the PC shows right now.</summary>
    public string QrText(params string[] hosts) =>
        new PairingLink(hosts.Length == 0 ? [TestPc.Address] : hosts, 5243, Pc.PairingCode, TestPc.Fingerprint, TestPc.Name).ToString();

    public async Task PairAsync()
    {
        await Link.StartAsync(TestContext.Current.CancellationToken);
        (await Link.PairAsync(QrText(), TestContext.Current.CancellationToken)).ShouldBe(PairStatus.Paired);
    }

    public void Dispose() => Link.Dispose();
}

/// <summary>Carries requests to the <see cref="TestPc"/>; hosts in <see cref="Down"/> don't answer.</summary>
internal sealed class TestTransport(TestPc pc) : IPcTransport
{
    private readonly List<string> _asked = [];

    public HashSet<string> Down { get; } = [];

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
