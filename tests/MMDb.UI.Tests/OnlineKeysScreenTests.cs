using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Settings → Online databases: adding, changing and removing the TMDB key and the Metron token.</summary>
public sealed class OnlineKeysScreenTests : ShelfTestContext
{
    private const string Dots = "••••••••••••";

    private static string Text(IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private static IElement Row(IRenderedComponent<Settings> page, string service) => page.Find($".apikey-row[data-service='{service}']");

    /// <summary>What a service's row shows after its name: "••••••••••••3f9a | Connected | Change".</summary>
    private static string Shows(IRenderedComponent<Settings> page, string service) =>
        string.Join(" | ", Row(page, service).QuerySelectorAll(".apikey, .sync-state, .warn, button.change-key").Select(Text));

    /// <summary>The PC starts with these keys saved in Settings earlier.</summary>
    private void HasKeys(string? tmdb = null, string? metron = null)
    {
        Online.KeyStore.Saved = new(tmdb, metron);
        Services.GetRequiredService<IKeysApi>().Load();
    }

    private static void Edit(IRenderedComponent<Settings> page, string service) => Row(page, service).QuerySelector("button.change-key")!.Click();

    private static void Type(IRenderedComponent<Settings> page, string key) => page.Find(".apikey-input").Input(key);

    [Fact]
    public void Without_keys_each_service_offers_to_add_one()
    {
        var page = Render<Settings>();

        Text(Row(page, "TMDB").QuerySelector(".lbl")!).ShouldBe("TMDB · movies, trailers");
        Shows(page, "TMDB").ShouldBe("Not set | Add key");
        Text(Row(page, "Metron").QuerySelector(".lbl")!).ShouldBe("Metron · comics");
        Shows(page, "Metron").ShouldBe("Not set | Add token");
        page.FindAll(".apikey-input").ShouldBeEmpty();
        Online.Keys.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void Upcitemdb_is_listed_as_needing_no_key()
    {
        var page = Render<Settings>();

        var rows = page.FindAll("section[aria-labelledby='online-heading'] .setrow");

        rows.Count.ShouldBe(3);
        Text(rows[1].QuerySelector(".lbl")!).ShouldBe("UPCitemdb · movie barcodes");
        Text(rows[1].QuerySelector(".apikey")!).ShouldBe("Free tier (no key)");
        rows[1].QuerySelectorAll("button").ShouldBeEmpty();
    }

    [Fact]
    public void A_pasted_key_is_checked_kept_and_shown_by_its_ending()
    {
        var page = Render<Settings>();
        Edit(page, "TMDB");

        Type(page, " tmdb-key-3f9a ");
        page.Find("button.save-key").Click();

        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldBe($"{Dots}3f9a | Connected | Change"));
        page.FindAll(".apikey-input").ShouldBeEmpty();
        Online.Keys.Asked.ShouldBe(["TMDB tmdb-key-3f9a"]);
        Online.KeyStore.Saved.ShouldBe(new SavedKeys("tmdb-key-3f9a", null));
        page.Markup.ShouldNotContain("tmdb-key-3f9a");
    }

    [Fact]
    public void The_box_says_what_to_paste_and_where_to_get_it()
    {
        var page = Render<Settings>();

        Edit(page, "Metron");

        var box = page.Find(".apikey-input");
        box.GetAttribute("type").ShouldBe("password");
        box.GetAttribute("aria-label").ShouldBe("Metron token");
        box.GetAttribute("placeholder").ShouldBe("Paste your Metron token");
        Text(page.Find(".apikey-help")).ShouldBe("Make an API token in your account on metron.cloud.");
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Save_waits_for_something_to_be_typed()
    {
        var page = Render<Settings>();
        Edit(page, "TMDB");
        page.Find("button.save-key").HasAttribute("disabled").ShouldBeTrue();

        Type(page, "   ");
        page.Find("button.save-key").HasAttribute("disabled").ShouldBeTrue();

        Type(page, "tmdb-key-3f9a");
        page.Find("button.save-key").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void While_the_service_is_asked_the_button_says_so()
    {
        var hold = Online.Keys.Hold = new();
        var page = Render<Settings>();
        Edit(page, "TMDB");
        Type(page, "tmdb-key-3f9a");

        page.Find("button.save-key").Click();

        var save = page.Find("button.save-key");
        save.TextContent.ShouldBe("Checking…");
        save.HasAttribute("disabled").ShouldBeTrue();
        page.Find("button.cancel-key").HasAttribute("disabled").ShouldBeTrue();
        hold.SetResult();
        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldEndWith("Connected | Change"));
    }

    [Fact]
    public void A_key_the_service_turns_down_is_not_kept_and_the_box_stays_open()
    {
        Online.Keys.Rejected.Add("mistyped");
        var page = Render<Settings>();
        Edit(page, "TMDB");
        Type(page, "mistyped");

        page.Find("button.save-key").Click();

        page.WaitForAssertion(() => page.Find(".apikey-help .warn").TextContent.ShouldBe("TMDB turned this key down, so it wasn't saved."));
        page.Find(".apikey-input").GetAttribute("value").ShouldBe("mistyped");
        Online.KeyStore.Saved.ShouldBeNull();

        Online.Keys.Rejected.Clear();
        page.Find("button.save-key").Click();

        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldEndWith("Connected | Change"));
        page.FindAll(".warn").ShouldBeEmpty();
    }

    [Fact]
    public void A_token_saved_while_offline_is_kept_and_marked_not_checked()
    {
        Online.Keys.Offline = true;
        var page = Render<Settings>();
        Edit(page, "Metron");
        Type(page, "metron-token-0002");

        page.Find("button.save-key").Click();

        page.WaitForAssertion(() => Shows(page, "Metron").ShouldBe($"{Dots}0002 | Not checked: couldn't reach Metron | Change"));
        Online.KeyStore.Saved.ShouldBe(new SavedKeys(null, "metron-token-0002"));
    }

    [Fact]
    public void Enter_saves_the_key()
    {
        var page = Render<Settings>();
        Edit(page, "TMDB");
        Type(page, "tmdb-key-3f9a");

        page.Find(".apikey-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldEndWith("Connected | Change"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancelling_changes_nothing(bool withEscape)
    {
        HasKeys(tmdb: "tmdb-key-0001");
        var page = Render<Settings>();
        Edit(page, "TMDB");
        Type(page, "another-key-9999");

        if (withEscape)
        {
            page.Find(".apikey-input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            page.Find("button.cancel-key").Click();
        }

        Shows(page, "TMDB").ShouldBe($"{Dots}0001 | Connected | Change");
        Online.KeyStore.Saved.ShouldBe(new SavedKeys("tmdb-key-0001", null));

        Edit(page, "TMDB");
        page.Find(".apikey-input").GetAttribute("value").ShouldBeNullOrEmpty("what was typed before is not kept");
    }

    [Fact]
    public void Opening_settings_asks_each_service_about_its_key()
    {
        HasKeys(tmdb: "tmdb-key-0001", metron: "metron-token-0002");
        Online.Keys.Rejected.Add("metron-token-0002");

        var page = Render<Settings>();

        page.WaitForAssertion(() => Shows(page, "Metron").ShouldBe($"{Dots}0002 | Metron turned this token down | Change"));
        Row(page, "Metron").QuerySelector(".warn").ShouldNotBeNull();
        Shows(page, "TMDB").ShouldBe($"{Dots}0001 | Connected | Change");
        Online.Keys.Asked.ShouldBe(["TMDB tmdb-key-0001", "Metron metron-token-0002"], ignoreOrder: true);
    }

    [Fact]
    public void A_key_shows_as_checking_until_its_service_answers()
    {
        HasKeys(tmdb: "tmdb-key-0001");
        var hold = Online.Keys.Hold = new();

        var page = Render<Settings>();

        Shows(page, "TMDB").ShouldBe($"{Dots}0001 | Checking… | Change");
        hold.SetResult();
        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldEndWith("Connected | Change"));
    }

    [Fact]
    public void A_key_can_be_removed_where_it_is_changed()
    {
        HasKeys(tmdb: "tmdb-key-0001", metron: "metron-token-0002");
        var page = Render<Settings>();
        Edit(page, "TMDB");

        var remove = page.Find("button.remove-key");
        remove.TextContent.ShouldBe("Remove key");
        remove.Click();

        Shows(page, "TMDB").ShouldBe("Not set | Add key");
        Online.KeyStore.Saved.ShouldBe(new SavedKeys(null, "metron-token-0002"));
    }

    [Fact]
    public void There_is_nothing_to_remove_before_a_key_is_added()
    {
        var page = Render<Settings>();

        Edit(page, "Metron");

        page.FindAll("button.remove-key").ShouldBeEmpty();
    }

    [Fact]
    public void One_key_is_typed_at_a_time()
    {
        var page = Render<Settings>();
        Edit(page, "TMDB");
        Type(page, "half-typed");

        Edit(page, "Metron");

        var box = page.FindAll(".apikey-input").ShouldHaveSingleItem();
        box.GetAttribute("aria-label").ShouldBe("Metron token");
        box.GetAttribute("value").ShouldBeNullOrEmpty();
        Shows(page, "TMDB").ShouldBe("Not set | Add key");
    }

    [Fact]
    public void A_key_changed_elsewhere_shows_without_reopening_settings()
    {
        var page = Render<Settings>();

        Services.GetRequiredService<IKeysApi>().SaveAsync(Contracts.Keys.OnlineService.Tmdb, "tmdb-key-0001", Ct);

        page.WaitForAssertion(() => Shows(page, "TMDB").ShouldBe($"{Dots}0001 | Connected | Change"));
    }
}
