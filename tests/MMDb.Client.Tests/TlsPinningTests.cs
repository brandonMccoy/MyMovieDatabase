using MMDb.Contracts.Phone;
using MMDb.Server.Phone;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>
/// The real thing on this machine: the PC's HTTPS server with its own certificate, and the phone's client
/// trusting that certificate and no other. Nothing leaves the computer.
/// </summary>
public sealed class TlsPinningTests : IAsyncLifetime
{
    private const string Loopback = "127.0.0.1";

    private readonly TempFolder _folder = new();
    private readonly MemoryLinkStore _store = new();
    private TestPc _pc = null!;
    private PhoneHost _host = null!;
    private PcLink _link = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int Port => _pc.Server.Status.Port;

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _host = await _pc.ListenAsync(_folder["phone.pfx"]);
        _link = new PcLink(_store, new TlsTransport(), new PhoneIdentity("Pixel 8"));
        await _link.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _link.Dispose();
        await _host.DisposeAsync();
        await _pc.DisposeAsync();
        _folder.Dispose();
    }

    private string QrText(string fingerprint) => new PairingLink([Loopback], Port, _pc.PairingCode, fingerprint, TestPc.Name).ToString();

    [Fact]
    public async Task The_server_listens_with_its_own_certificate()
    {
        _pc.Server.Status.State.ShouldBe(PhoneServerState.Listening);
        Port.ShouldBeGreaterThan(0);
        _pc.Server.Fingerprint.ShouldNotBeNull().ShouldMatch("^[0-9A-F]{64}$");
        File.Exists(_folder["phone.pfx"]).ShouldBeTrue();

        await _host.DisposeAsync();
        _pc.Server.Status.State.ShouldBe(PhoneServerState.Off);
    }

    [Fact]
    public async Task A_phone_that_scanned_the_qr_code_talks_to_the_pc_over_https()
    {
        (await _link.PairAsync(QrText(_pc.Server.Fingerprint!), Ct)).ShouldBe(PairStatus.Paired);

        (await new HttpCollectionApi(_link).ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
        _link.Address.ShouldBe($"{Loopback}:{Port}");
    }

    [Fact]
    public async Task A_pc_with_another_certificate_than_the_qr_codes_is_refused_before_the_code_is_sent()
    {
        var someoneElse = new string('A', 64);

        (await _link.PairAsync(QrText(someoneElse), Ct)).ShouldBe(PairStatus.WrongPc);

        _link.State.ShouldBe(PcLinkState.NotPaired);
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldBeEmpty();
        _pc.Phone.PairingCode().Code.ShouldBe(_pc.PairingCode, "the code was never tried");
    }

    [Fact]
    public async Task Pairing_by_typed_address_remembers_the_certificate_the_pc_showed()
    {
        (await _link.PairAsync($"{Loopback}:{Port}", _pc.PairingCode, Ct)).ShouldBe(PairStatus.Paired);

        _store.Record!.Fingerprint.ShouldBe(_pc.Server.Fingerprint);
        (await _link.CheckAsync(Ct)).ShouldBe(PcLinkState.Connected);
    }

    [Fact]
    public async Task A_paired_phone_wont_talk_to_another_machine_at_the_pcs_address()
    {
        await _link.PairAsync(QrText(_pc.Server.Fingerprint!), Ct);
        var port = Port;
        await _host.DisposeAsync();

        // The same address, but a certificate of its own: not the PC this phone paired with.
        _host = await _pc.ListenAsync(_folder["another.pfx"], port);

        _pc.Server.Status.State.ShouldBe(PhoneServerState.Listening);
        (await _link.CheckAsync(Ct)).ShouldBe(PcLinkState.Unreachable);
    }

    [Fact]
    public async Task A_port_that_is_taken_is_reported_not_thrown()
    {
        await using var second = await _pc.ListenAsync(_folder["second.pfx"], Port);

        _pc.Server.Status.State.ShouldBe(PhoneServerState.Failed);
        _pc.Server.Status.Problem.ShouldNotBeNullOrWhiteSpace();
    }
}
