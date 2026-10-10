using MMDb.Application.Devices;

namespace MMDb.Application.Tests;

/// <summary>Pairing a phone with the one-time code, and the token it then proves itself with.</summary>
public sealed class DevicePairingTests : IAsyncLifetime
{
    private TestShelf _shelf = null!;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public async ValueTask DisposeAsync() => await _shelf.DisposeAsync();

    private Task<NewPairing?> PairAsync(string? code, string name = "Pixel 8") => _shelf.Devices((devices, ct) => devices.PairAsync(code, name, ct));

    private async Task<string?> WhoIsAsync(string? token) => (await _shelf.Devices((devices, ct) => devices.AuthenticateAsync(token, ct)))?.Name;

    [Fact]
    public void The_code_is_six_digits_and_stays_the_same_until_it_expires()
    {
        var code = _shelf.Desk.Current();

        code.Value.ShouldMatch("^[0-9]{6}$");
        code.ExpiresAt.ShouldBe(TestShelf.Start + PairingDesk.CodeLifetime);

        _shelf.Clock.Advance(PairingDesk.CodeLifetime - TimeSpan.FromSeconds(1));
        _shelf.Desk.Current().ShouldBe(code);

        _shelf.Clock.Advance(TimeSpan.FromSeconds(1));
        _shelf.Desk.Current().ExpiresAt.ShouldBe(_shelf.Clock.GetUtcNow() + PairingDesk.CodeLifetime);
    }

    [Fact]
    public async Task The_code_pairs_a_phone_which_then_proves_itself_with_its_token()
    {
        var pairing = (await PairAsync(_shelf.Desk.Current().Value)).ShouldNotBeNull();

        pairing.Token.Length.ShouldBeGreaterThanOrEqualTo(40);
        pairing.Device.Name.ShouldBe("Pixel 8");
        (await WhoIsAsync(pairing.Token)).ShouldBe("Pixel 8");
        var listed = (await _shelf.Devices((devices, ct) => devices.ListAsync(ct))).ShouldHaveSingleItem();
        listed.PairedAt.ShouldBe(TestShelf.Start);
        listed.TokenHash.ShouldNotContain(pairing.Token);
    }

    [Fact]
    public async Task Spaces_typed_in_the_code_are_ignored()
    {
        var code = _shelf.Desk.Current().Value;

        (await PairAsync($" {code[..3]} {code[3..]} ")).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var code = _shelf.Desk.Current().Value;
        (await PairAsync(code)).ShouldNotBeNull();

        (await PairAsync(code, "Another phone")).ShouldBeNull();
    }

    [Fact]
    public async Task An_expired_code_is_turned_down()
    {
        var code = _shelf.Desk.Current().Value;
        _shelf.Clock.Advance(PairingDesk.CodeLifetime);

        (await PairAsync(code)).ShouldBeNull();
    }

    [Fact]
    public async Task No_code_works_before_the_pc_has_shown_one()
    {
        foreach (var guess in new[] { "000000", "", null })
        {
            (await PairAsync(guess)).ShouldBeNull();
        }

        (await _shelf.Devices((devices, ct) => devices.ListAsync(ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Guessing_wrong_a_few_times_throws_the_code_away_and_closes_pairing_for_a_minute()
    {
        var code = _shelf.Desk.Current().Value;
        var wrong = code == "111111" ? "222222" : "111111";
        for (var i = 0; i < PairingDesk.MaxWrongGuesses; i++)
        {
            (await PairAsync(wrong)).ShouldBeNull();
        }

        (await PairAsync(code)).ShouldBeNull("the code that was being guessed at is gone");
        var next = _shelf.Desk.Current().Value;
        (await PairAsync(next)).ShouldBeNull("pairing is closed for a minute");

        _shelf.Clock.Advance(PairingDesk.LockoutAfterGuessing);
        (await PairAsync(_shelf.Desk.Current().Value)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_few_typos_dont_cost_the_code()
    {
        var code = _shelf.Desk.Current().Value;
        var wrong = code == "111111" ? "222222" : "111111";
        for (var i = 0; i < PairingDesk.MaxWrongGuesses - 1; i++)
        {
            (await PairAsync(wrong)).ShouldBeNull();
        }

        (await PairAsync(code)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task An_unknown_token_is_nobody(string? token)
    {
        await PairAsync(_shelf.Desk.Current().Value);

        (await WhoIsAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task An_unpaired_phone_is_no_longer_let_in()
    {
        var pairing = (await PairAsync(_shelf.Desk.Current().Value)).ShouldNotBeNull();

        (await _shelf.Devices((devices, ct) => devices.UnpairAsync(pairing.Device.Id, ct))).ShouldBeTrue();

        (await WhoIsAsync(pairing.Token)).ShouldBeNull();
        (await _shelf.Devices((devices, ct) => devices.ListAsync(ct))).ShouldBeEmpty();
        (await _shelf.Devices((devices, ct) => devices.UnpairAsync(pairing.Device.Id, ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Phones_are_listed_newest_pairing_first_with_when_they_were_last_seen()
    {
        var first = (await PairAsync(_shelf.Desk.Current().Value, "Old phone")).ShouldNotBeNull();
        _shelf.Clock.Advance(TimeSpan.FromDays(1));
        await PairAsync(_shelf.Desk.Current().Value, "New phone");
        _shelf.Clock.Advance(TimeSpan.FromHours(1));
        await WhoIsAsync(first.Token);

        var devices = await _shelf.Devices((d, ct) => d.ListAsync(ct));

        devices.Select(d => (d.Name, d.LastSeenAt)).ShouldBe(
        [
            ("New phone", TestShelf.Start.AddDays(1)),
            ("Old phone", TestShelf.Start.AddDays(1).AddHours(1)),
        ]);
    }

    [Fact]
    public async Task The_desk_says_when_the_code_or_the_phones_change()
    {
        var changes = 0;
        _shelf.Desk.Changed += () => changes++;
        var code = _shelf.Desk.Current().Value;

        var pairing = (await PairAsync(code)).ShouldNotBeNull();
        changes.ShouldBe(2, "the code was used up, and a phone was added");

        await WhoIsAsync(pairing.Token);
        changes.ShouldBe(2, "seen a moment ago: nothing new to show");

        _shelf.Clock.Advance(TimeSpan.FromMinutes(5));
        await WhoIsAsync(pairing.Token);
        changes.ShouldBe(3);

        await _shelf.Devices((devices, ct) => devices.UnpairAsync(pairing.Device.Id, ct));
        changes.ShouldBe(4);
    }
}
