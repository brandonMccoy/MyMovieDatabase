using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using MMDb.Contracts.Phone;
using MMDb.Server.Phone;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>What the PC's Settings screen is told about pairing, and the PC's certificate.</summary>
public sealed class PhonePairingTests : IAsyncLifetime
{
    private TestPc _pc = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _pc = await TestPc.StartAsync();

    public async ValueTask DisposeAsync() => await _pc.DisposeAsync();

    [Fact]
    public void The_pairing_code_comes_with_its_expiry_and_a_square_qr_code()
    {
        var code = _pc.Phone.PairingCode();

        code.Code.ShouldMatch("^[0-9]{6}$");
        code.ExpiresAt.ShouldBe(TestPc.Start.AddMinutes(5));
        code.QrRows.Count.ShouldBeGreaterThan(21);
        code.QrRows.ShouldAllBe(row => row.Length == code.QrRows.Count && row.Trim('0', '1').Length == 0);

        // A finder pattern sits in the top-left corner, inside the quiet zone.
        var quiet = code.QrRows.TakeWhile(row => !row.Contains('1', StringComparison.Ordinal)).Count();
        code.QrRows[quiet].Substring(quiet, 7).ShouldBe("1111111");
        code.QrRows[quiet + 1].Substring(quiet, 7).ShouldBe("1000001");
    }

    [Fact]
    public void The_qr_code_changes_with_the_code_and_is_left_out_until_the_server_listens()
    {
        var first = _pc.Phone.PairingCode();
        _pc.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = _pc.Phone.PairingCode();

        _pc.Server.Stopped();
        var off = _pc.Phone.PairingCode();

        second.ExpiresAt.ShouldBe(TestPc.Start.AddMinutes(10));
        second.QrRows.ShouldNotBe(first.QrRows);
        off.Code.ShouldBe(second.Code);
        off.QrRows.ShouldBeEmpty();
    }

    [Fact]
    public void The_server_reports_where_phones_reach_it()
    {
        _pc.Phone.Server.ShouldBeEquivalentTo(new PhoneServerDto(PhoneServerState.Listening, 5243, [TestPc.Address]));

        _pc.Server.Failed(5243, "Port 5243 is in use.");

        _pc.Phone.Server.ShouldBeEquivalentTo(new PhoneServerDto(PhoneServerState.Failed, 5243, [], "Port 5243 is in use."));
    }

    [Fact]
    public async Task Paired_phones_are_listed_and_can_be_unpaired()
    {
        await _pc.PairAsync("Pixel 8");
        _pc.Clock.Advance(TimeSpan.FromHours(1));
        await _pc.PairAsync("Galaxy S24");

        var devices = await _pc.Phone.ListDevicesAsync(Ct);
        devices.Select(d => (d.Name, d.PairedAt, d.LastSeenAt)).ShouldBe(
        [
            ("Galaxy S24", TestPc.Start.AddHours(1), TestPc.Start.AddHours(1)),
            ("Pixel 8", TestPc.Start, TestPc.Start),
        ]);

        (await _pc.Phone.UnpairAsync(devices[1].Id, Ct)).ShouldBeTrue();
        (await _pc.Phone.UnpairAsync(devices[1].Id, Ct)).ShouldBeFalse();
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem().Name.ShouldBe("Galaxy S24");
    }

    [Fact]
    public async Task The_settings_screen_is_told_when_something_changes()
    {
        var changes = 0;
        _pc.Phone.Changed += () => changes++;

        await _pc.PairAsync();
        changes.ShouldBe(2, "the code was used up and a phone was added");

        await _pc.Phone.UnpairAsync((await _pc.Phone.ListDevicesAsync(Ct))[0].Id, Ct);
        changes.ShouldBe(3);

        _pc.Server.Stopped();
        changes.ShouldBe(4);
    }
}

public sealed class PhoneAddressTests
{
    [Fact]
    public void The_home_network_address_comes_before_a_vpns_and_virtual_adapters()
    {
        var addresses = PhoneHost.OrderAddresses(
        [
            ((NetworkInterfaceType)53, true, "10.5.0.2"),                    // a VPN tunnel
            (NetworkInterfaceType.Ethernet, false, "172.20.0.1"),          // a virtual switch: no gateway
            (NetworkInterfaceType.Wireless80211, true, "192.168.1.65"),
            (NetworkInterfaceType.Loopback, false, "127.0.0.1"),
            (NetworkInterfaceType.Ethernet, false, "169.254.12.7"),        // unplugged, self-assigned
            (NetworkInterfaceType.Wireless80211, true, "192.168.1.65"),
        ]);

        addresses.ShouldBe(["192.168.1.65", "172.20.0.1", "10.5.0.2"]);
    }

    [Fact]
    public void A_pc_with_no_network_has_no_addresses()
    {
        PhoneHost.OrderAddresses([(NetworkInterfaceType.Loopback, false, "127.0.0.1")]).ShouldBeEmpty();
    }
}

public sealed class PhoneCertificateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();

    private string PfxPath => Path.Combine(_folder.Path, "data", "phone.pfx");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_certificate_is_made_once_and_kept_so_phones_keep_recognizing_the_pc()
    {
        using var first = PhoneCertificate.LoadOrCreate(PfxPath, Now);
        using var second = PhoneCertificate.LoadOrCreate(PfxPath, Now.AddYears(5));

        File.Exists(PfxPath).ShouldBeTrue();
        first.HasPrivateKey.ShouldBeTrue();
        second.HasPrivateKey.ShouldBeTrue();
        PhoneCertificate.Fingerprint(first).ShouldMatch("^[0-9A-F]{64}$");
        PhoneCertificate.Fingerprint(second).ShouldBe(PhoneCertificate.Fingerprint(first));
    }

    [Fact]
    public void The_certificate_is_for_a_server_and_good_for_years()
    {
        using var certificate = PhoneCertificate.LoadOrCreate(PfxPath, Now);

        certificate.NotBefore.ToUniversalTime().ShouldBeLessThan(Now.UtcDateTime);
        certificate.NotAfter.ToUniversalTime().ShouldBeGreaterThan(Now.UtcDateTime.AddYears(15));
        certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ShouldHaveSingleItem()
            .EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().ShouldHaveSingleItem().Value.ShouldBe("1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void A_damaged_or_nearly_expired_certificate_is_replaced()
    {
        using var first = PhoneCertificate.LoadOrCreate(PfxPath, Now);

        using var afterExpiry = PhoneCertificate.LoadOrCreate(PfxPath, Now.AddYears(20));
        File.WriteAllText(PfxPath, "not a certificate");
        using var afterDamage = PhoneCertificate.LoadOrCreate(PfxPath, Now.AddYears(20));

        PhoneCertificate.Fingerprint(afterExpiry).ShouldNotBe(PhoneCertificate.Fingerprint(first));
        PhoneCertificate.Fingerprint(afterDamage).ShouldNotBe(PhoneCertificate.Fingerprint(afterExpiry));
        afterDamage.HasPrivateKey.ShouldBeTrue();
    }
}
