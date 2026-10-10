using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>Pairing the phone with the PC, and knowing whether the PC is there.</summary>
public sealed class PcLinkTests : IAsyncLifetime
{
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PcLink Link => _phone.Link;

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _phone = new TestPhone(_pc);
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
    }

    [Fact]
    public async Task A_new_phone_isnt_paired()
    {
        await Link.StartAsync(Ct);

        Link.State.ShouldBe(PcLinkState.NotPaired);
        Link.PcName.ShouldBeNull();
        Link.Address.ShouldBeNull();
        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.NotPaired);
        await Should.ThrowAsync<PcUnavailableException>(() => _phone.Api.ListMoviesAsync(new(), Ct));
        _phone.Transport.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scanning_the_pcs_qr_code_pairs_the_phone()
    {
        await Link.StartAsync(Ct);

        var status = await Link.PairAsync(_phone.QrText(), Ct);

        status.ShouldBe(PairStatus.Paired);
        Link.State.ShouldBe(PcLinkState.Connected);
        Link.PcName.ShouldBe(TestPc.Name);
        Link.Address.ShouldBe("192.168.1.24:5243");
        _phone.Store.Record.ShouldNotBeNull().ShouldSatisfyAllConditions(
            r => r.Hosts.ShouldBe([TestPc.Address]),
            r => r.Port.ShouldBe(5243),
            r => r.Fingerprint.ShouldBe(TestPc.Fingerprint),
            r => r.PcName.ShouldBe(TestPc.Name),
            r => r.Token.ShouldNotBeNullOrWhiteSpace());
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldHaveSingleItem().Name.ShouldBe("Pixel 8");
        (await _phone.Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("192.168.1.24")]
    [InlineData("192.168.1.24:5243")]
    [InlineData(" https://192.168.1.24:5243/ ")]
    public async Task Typing_the_pcs_address_and_code_pairs_the_phone(string address)
    {
        await Link.StartAsync(Ct);
        var code = _pc.PairingCode;

        var status = await Link.PairAsync(address, $"{code[..3]} {code[3..]}", Ct);

        status.ShouldBe(PairStatus.Paired);
        Link.Address.ShouldBe("192.168.1.24:5243");
        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.Connected);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("shelf://pair?h=192.168.1.24&p=5243&c=123456")]
    [InlineData("")]
    public async Task Text_that_isnt_a_pairing_code_is_turned_down_without_asking_anyone(string qrText)
    {
        await Link.StartAsync(Ct);

        (await Link.PairAsync(qrText, Ct)).ShouldBe(PairStatus.NotACode);

        _phone.Transport.Asked.ShouldBeEmpty();
        Link.State.ShouldBe(PcLinkState.NotPaired);
    }

    [Theory]
    [InlineData("", "123456")]
    [InlineData("192.168.1.24", "")]
    [InlineData("192.168.1.24:port", "123456")]
    [InlineData("192.168.1.24:99999", "123456")]
    [InlineData("not an address", "123456")]
    public async Task A_typed_address_or_code_that_cant_be_right_is_turned_down_without_asking_anyone(string address, string code)
    {
        await Link.StartAsync(Ct);

        (await Link.PairAsync(address, code, Ct)).ShouldBe(PairStatus.NotACode);

        _phone.Transport.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_code_doesnt_pair()
    {
        await Link.StartAsync(Ct);
        var wrong = _pc.PairingCode == "111111" ? "222222" : "111111";

        (await Link.PairAsync(TestPc.Address, wrong, Ct)).ShouldBe(PairStatus.WrongCode);

        Link.State.ShouldBe(PcLinkState.NotPaired);
        _phone.Store.Record.ShouldBeNull();
        (await _pc.Phone.ListDevicesAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pc_that_doesnt_answer_cant_be_paired_with()
    {
        await Link.StartAsync(Ct);
        _phone.Transport.Down.Add(TestPc.Address);

        (await Link.PairAsync(_phone.QrText(), Ct)).ShouldBe(PairStatus.Unreachable);

        Link.State.ShouldBe(PcLinkState.NotPaired);
    }

    [Fact]
    public async Task The_qr_codes_addresses_are_tried_in_turn_and_the_one_that_answers_is_used_first_from_then_on()
    {
        await Link.StartAsync(Ct);
        _phone.Transport.Down.Add("172.20.0.1");

        (await Link.PairAsync(_phone.QrText("172.20.0.1", TestPc.Address), Ct)).ShouldBe(PairStatus.Paired);
        await _phone.Api.ListMoviesAsync(new(), Ct);

        Link.Address.ShouldBe("192.168.1.24:5243");
        _phone.Store.Record!.Hosts.ShouldBe([TestPc.Address, "172.20.0.1"]);
        _phone.Transport.Asked.ShouldBe(["172.20.0.1/api/v1/pair", "192.168.1.24/api/v1/pair", "192.168.1.24/api/v1/movies"]);
    }

    [Fact]
    public async Task When_the_pcs_address_changes_the_phone_finds_it_at_another_one_it_knows()
    {
        await Link.StartAsync(Ct);
        await Link.PairAsync(_phone.QrText(TestPc.Address, "10.0.0.7"), Ct);
        _phone.Transport.Down.Add(TestPc.Address);

        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.Connected);

        Link.Address.ShouldBe("10.0.0.7:5243");
        _phone.Store.Record!.Hosts.ShouldBe(["10.0.0.7", TestPc.Address]);
    }

    [Fact]
    public async Task A_paired_phone_picks_up_where_it_left_off()
    {
        await _phone.PairAsync();
        using var restarted = new PcLink(_phone.Store, _phone.Transport, new PhoneIdentity("Pixel 8"));

        await restarted.StartAsync(Ct);

        restarted.State.ShouldBe(PcLinkState.Unknown);
        restarted.PcName.ShouldBe(TestPc.Name);
        (await restarted.CheckAsync(Ct)).ShouldBe(PcLinkState.Connected);
        (await new HttpCollectionApi(restarted).ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_phone_notices_when_the_pc_goes_away_and_comes_back()
    {
        await _phone.PairAsync();
        var states = new List<PcLinkState>();
        Link.Changed += () => states.Add(Link.State);

        _phone.Transport.Down.Add(TestPc.Address);
        await Should.ThrowAsync<PcUnavailableException>(() => _phone.Api.ListMoviesAsync(new(), Ct));
        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.Unreachable);

        _phone.Transport.Down.Clear();
        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.Connected);

        states.ShouldBe([PcLinkState.Unreachable, PcLinkState.Connected]);
    }

    [Fact]
    public async Task A_phone_unpaired_on_the_pc_finds_out_on_its_next_request()
    {
        await _phone.PairAsync();
        await _pc.Phone.UnpairAsync((await _pc.Phone.ListDevicesAsync(Ct))[0].Id, Ct);

        await Should.ThrowAsync<PcUnavailableException>(() => _phone.Api.ListMoviesAsync(new(), Ct));

        Link.State.ShouldBe(PcLinkState.Unpaired);
        (await Link.CheckAsync(Ct)).ShouldBe(PcLinkState.Unpaired);
    }

    [Fact]
    public async Task Forgetting_the_pc_leaves_the_phone_ready_to_pair_again()
    {
        await _phone.PairAsync();

        await Link.ForgetAsync(Ct);

        Link.State.ShouldBe(PcLinkState.NotPaired);
        Link.PcName.ShouldBeNull();
        _phone.Store.Record.ShouldBeNull();
        await Should.ThrowAsync<PcUnavailableException>(() => _phone.Api.ListMoviesAsync(new(), Ct));
        (await Link.PairAsync(_phone.QrText(), Ct)).ShouldBe(PairStatus.Paired);
    }
}

public sealed class PairingLinkTests
{
    private const string Fingerprint = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    [Fact]
    public void The_qr_text_carries_where_the_pc_is_the_code_and_the_certificates_fingerprint()
    {
        var link = new PairingLink(["192.168.1.24", "10.0.0.7"], 5243, "482913", Fingerprint, "Bob's PC");

        var text = link.ToString();

        text.ShouldBe($"shelf://pair?h=192.168.1.24,10.0.0.7&p=5243&c=482913&f={Fingerprint}&n=Bob%27s%20PC");
        PairingLink.TryParse(text, out var parsed).ShouldBeTrue();
        Wire.Json(parsed).ShouldBe(Wire.Json(link));
    }

    [Fact]
    public void The_pc_name_is_optional_and_the_fingerprint_may_be_lower_case()
    {
        PairingLink.TryParse($" shelf://pair?h=192.168.1.24&p=5243&c=482913&f={Fingerprint.ToLowerInvariant()} ", out var parsed).ShouldBeTrue();

        Wire.Json(parsed).ShouldBe(Wire.Json(new PairingLink(["192.168.1.24"], 5243, "482913", Fingerprint)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/?h=192.168.1.24&p=5243&c=482913&f=AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]
    [InlineData("shelf://pair?p=5243&c=482913&f=AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]            // no host
    [InlineData("shelf://pair?h=not a host&p=5243&c=482913&f=AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]
    [InlineData("shelf://pair?h=192.168.1.24&p=0&c=482913&f=AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]
    [InlineData("shelf://pair?h=192.168.1.24&p=5243&f=AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]      // no code
    [InlineData("shelf://pair?h=192.168.1.24&p=5243&c=482913")]                                                                    // no fingerprint
    [InlineData("shelf://pair?h=192.168.1.24&p=5243&c=482913&f=AB12")]
    [InlineData("shelf://pair?h=192.168.1.24&p=5243&c=482913&f=ZZ12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12")]
    public void Anything_else_isnt_a_pairing_code(string? text)
    {
        PairingLink.TryParse(text, out _).ShouldBeFalse();
    }
}
