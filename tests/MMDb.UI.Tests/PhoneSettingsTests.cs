using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MMDb.Contracts;
using MMDb.Server.Phone;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Settings → Phone on the PC: the pairing code and the phones already paired.</summary>
public sealed class PhoneSettingsTests : ShelfTestContext
{
    private const string Fingerprint = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    private PhoneServer Server => Services.GetRequiredService<PhoneServer>();

    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private IRenderedComponent<Settings> RenderListening()
    {
        Server.Listening(5243, ["192.168.1.24", "172.20.0.1"], Fingerprint);
        var page = Render<Settings>();
        page.WaitForElement(".pair");
        return page;
    }

    private async Task<string> PairAsync(string name)
    {
        var code = Services.GetRequiredService<IPhoneApi>().PairingCode().Code;
        var pairing = await Services.GetRequiredService<DeviceGate>().PairAsync(code, name, Ct);
        return pairing.ShouldNotBeNull().Token;
    }

    private static string Code(IRenderedComponent<Settings> page) => page.Find(".pair .code").TextContent;

    [Fact]
    public void The_phone_section_shows_the_code_to_scan_or_type_and_where_the_pc_is()
    {
        var page = RenderListening();

        Text(page.Find("#phone-heading")).ShouldBe("Phone");
        Code(page).ShouldMatch("^[0-9]{3} [0-9]{3}$");
        Text(page.Find(".expiry")).ShouldBe("Expires in 5:00 · PC address 192.168.1.24:5243");
        var qr = page.Find("svg.qr");
        qr.GetAttribute("viewBox")!.ShouldMatch("^0 0 ([0-9]+) \\1$");
        qr.QuerySelector("path")!.GetAttribute("d")!.ShouldStartWith("M");
        page.FindAll(".device").ShouldBeEmpty();
    }

    [Fact]
    public void The_code_counts_down_and_is_replaced_when_it_runs_out()
    {
        var page = RenderListening();
        var first = Code(page);

        Clock.Advance(TimeSpan.FromSeconds(8));
        page.WaitForAssertion(() => Text(page.Find(".expiry")).ShouldStartWith("Expires in 4:52"));
        Code(page).ShouldBe(first);

        Clock.Advance(TimeSpan.FromMinutes(5));
        page.WaitForAssertion(() => Text(page.Find(".expiry")).ShouldStartWith("Expires in 5:00"));
    }

    [Fact]
    public async Task A_phone_that_pairs_shows_up_at_once_and_can_be_unpaired()
    {
        var page = RenderListening();

        var token = await PairAsync("Pixel 8");

        page.WaitForElement(".device");
        Text(page.Find(".device")).ShouldBe("Pixel 8 Connected · paired 15 Jan 2026 Unpair");
        page.Find(".device .dot").ClassList.ShouldContain("on");
        Text(page.Find(".expiry")).ShouldStartWith("Expires in 5:00", customMessage: "the used code was replaced");

        page.Find(".device button").Click();

        page.WaitForAssertion(() => page.FindAll(".device").ShouldBeEmpty());
        (await Services.GetRequiredService<DeviceGate>().AuthenticateAsync(token, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_phone_not_heard_from_lately_shows_when_it_was_last_seen()
    {
        await PairAsync("Pixel 8");
        Clock.Advance(TimeSpan.FromMinutes(10));
        await PairAsync("Galaxy S24");

        var page = RenderListening();

        page.WaitForAssertion(() => page.FindAll(".device").Select(d => Text(d)).ShouldBe(
        [
            "Galaxy S24 Connected · paired 15 Jan 2026 Unpair",
            "Pixel 8 Last seen 10 min ago · paired 15 Jan 2026 Unpair",
        ]));
        page.FindAll(".device .dot").Select(d => d.ClassList.Contains("on")).ShouldBe([true, false]);
    }

    [Fact]
    public async Task A_phone_stops_counting_as_connected_two_minutes_after_it_was_last_heard_from()
    {
        var token = await PairAsync("Pixel 8");
        var page = RenderListening();
        page.WaitForElement(".device");

        Clock.Advance(TimeSpan.FromMinutes(3));
        page.WaitForAssertion(() => Text(page.Find(".device .muted")).ShouldStartWith("Last seen 3 min ago"));

        await Services.GetRequiredService<DeviceGate>().AuthenticateAsync(token, Ct);
        page.WaitForAssertion(() => Text(page.Find(".device .muted")).ShouldStartWith("Connected"));
    }

    [Fact]
    public async Task Before_the_server_is_up_there_is_no_code_but_paired_phones_are_listed()
    {
        Server.Listening(5243, ["192.168.1.24"], Fingerprint);
        await PairAsync("Pixel 8");
        Server.Stopped();

        var page = Render<Settings>();

        page.WaitForElement(".device");
        Text(page.Find("section[aria-labelledby='phone-heading'] > p")).ShouldBe("Getting ready for phones…");
        page.FindAll(".pair").ShouldBeEmpty();

        Server.Listening(5243, ["192.168.1.24"], Fingerprint);
        page.WaitForElement(".pair");
    }

    [Fact]
    public void A_port_that_couldnt_be_opened_is_explained()
    {
        Server.Failed(5243, "Only one usage of each socket address is normally permitted");

        var page = Render<Settings>();

        Text(page.WaitForElement("section[aria-labelledby='phone-heading'] .warn")).ShouldBe(
            "Phones can't connect: Shelf couldn't open port 5243 on this PC (Only one usage of each socket address is normally permitted). Close the other program using it, then restart Shelf.");
        page.FindAll(".pair").ShouldBeEmpty();
    }

    [Fact]
    public void A_pc_that_isnt_on_a_network_says_so_instead_of_an_address()
    {
        Server.Listening(5243, [], Fingerprint);

        var page = Render<Settings>();

        Text(page.WaitForElement(".expiry")).ShouldBe("Expires in 5:00 · this PC isn't on a network");
    }
}
