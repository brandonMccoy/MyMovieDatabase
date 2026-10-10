using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.Application.Vault;
using MMDb.UI.Components;
using MMDb.UI.Layout;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Settings → Obsidian vault, the vault line in the sidebar, and the note link on a film's page.</summary>
public sealed class VaultScreenTests : ShelfTestContext
{
    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

    private static string Text(AngleSharp.Dom.IElement element) => string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task UseVaultAsync()
    {
        (await VaultApi.SetFolderAsync(VaultFolder, Ct)).ShouldBeTrue();
    }

    [Fact]
    public void Before_a_folder_is_chosen_settings_offers_to_choose_one()
    {
        var page = Render<Settings>();

        page.Find(".setrow .folder").TextContent.ShouldBe("Not set");
        page.Find("button.choose").TextContent.Trim().ShouldBe("Choose folder");
        page.FindAll("button.rebuild").ShouldBeEmpty();
    }

    [Fact]
    public async Task Choosing_a_folder_writes_the_vault_and_shows_when()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        FolderPicker.Answer = VaultFolder;
        var page = Render<Settings>();

        page.Find("button.choose").Click();

        page.WaitForAssertion(() => Text(page.Find(".sync-state")).ShouldBe("Just now · 2 notes"));
        page.Find(".setrow .folder").TextContent.ShouldBe(VaultFolder);
        page.Find("button.choose").TextContent.Trim().ShouldBe("Change");
        Vault.Calls.ShouldBe([$"sync {VaultFolder} (2 films)"]);
    }

    [Fact]
    public void Cancelling_the_folder_dialog_changes_nothing()
    {
        var page = Render<Settings>();

        page.Find("button.choose").Click();

        FolderPicker.Opened.ShouldBe(1);
        page.Find(".setrow .folder").TextContent.ShouldBe("Not set");
        Vault.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_chosen_folder_that_has_gone_is_turned_down()
    {
        var gone = Path.Combine(VaultFolder, "gone");
        FolderPicker.Answer = gone;
        var page = Render<Settings>();

        page.Find("button.choose").Click();

        page.WaitForAssertion(() => page.Find(".warn").TextContent.ShouldBe($"{gone} isn't there any more. Choose another folder."));
        page.Find(".setrow .folder").TextContent.ShouldBe("Not set");
    }

    [Fact]
    public async Task Rebuild_writes_the_vault_again()
    {
        await UseVaultAsync();
        var page = Render<Settings>();

        page.Find("button.rebuild").Click();

        page.WaitForAssertion(() => Vault.Calls.Count.ShouldBe(2));
        Vault.Calls[^1].ShouldBe($"sync {VaultFolder} (0 films)");
    }

    [Fact]
    public async Task A_failed_write_says_what_went_wrong()
    {
        await UseVaultAsync();
        Vault.FailWith = VaultProblem.CannotWrite;
        await SeedAsync(Heat);

        var page = Render<Settings>();

        page.Find(".sync-state.warn").TextContent.ShouldBe("Couldn't write to the vault: Access denied");
    }

    [Fact]
    public async Task A_missing_folder_says_how_to_fix_it()
    {
        await UseVaultAsync();
        Vault.FailWith = VaultProblem.FolderMissing;
        await VaultApi.RebuildAsync(Ct);

        var page = Render<Settings>();

        page.Find(".sync-state.warn").TextContent.ShouldBe("Folder not found. Reconnect its drive, or choose the folder again.");
    }

    [Fact]
    public void The_sidebar_says_when_there_is_no_vault()
    {
        Text(RenderLayout().Find(".vault-line")).ShouldBe("Vault not set up");
    }

    [Fact]
    public async Task The_sidebar_follows_the_vault_and_its_time_moves_on()
    {
        var layout = RenderLayout();

        await UseVaultAsync();
        layout.WaitForAssertion(() => Text(layout.Find(".vault-line")).ShouldBe("Vault synced · just now"));

        Clock.Advance(TimeSpan.FromMinutes(5));
        layout.WaitForAssertion(() => Text(layout.Find(".vault-line")).ShouldBe("Vault synced · 5 min ago"));
    }

    [Fact]
    public async Task The_sidebar_warns_when_the_vault_wasnt_updated()
    {
        await UseVaultAsync();
        Vault.FailWith = VaultProblem.CannotWrite;
        await SeedAsync(Heat);

        var line = RenderLayout().Find(".vault-line");

        Text(line).ShouldBe("Vault not updated");
        line.ClassList.ShouldContain("warn");
    }

    [Fact]
    public void On_settings_the_settings_link_is_lit_and_movies_is_not()
    {
        Navigation.NavigateTo("settings");

        var layout = RenderLayout();

        layout.FindAll(".nav a.active").Select(a => a.TextContent.Trim()).ShouldBe(["Settings"]);
    }

    [Fact]
    public async Task A_films_page_links_to_its_note_in_obsidian()
    {
        await UseVaultAsync();
        var heat = await SeedAsync(Heat);

        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));

        var link = page.WaitForElement("a.vault");
        link.TextContent.Trim().ShouldBe("Movies/Heat (1995).md");
        var expected = Path.GetFullPath(Path.Combine(VaultFolder, "Movies/Heat (1995).md"));
        link.GetAttribute("href").ShouldBe("obsidian://open?path=" + Uri.EscapeDataString(expected));
    }

    [Fact]
    public async Task Without_a_vault_a_films_page_has_no_note_link()
    {
        var heat = await SeedAsync(Heat);

        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));

        page.WaitForElement("h1");
        page.FindAll("a.vault").ShouldBeEmpty();
    }
}

public sealed class AgoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 15, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(2 * 3600, "today at 13:30")]
    [InlineData(20 * 3600, "8 Oct at 19:30")]
    [InlineData(400 * 86400, "4 Sep 2025")]
    public void Describes_how_long_ago(int secondsAgo, string expected) =>
        Ago.Describe(Now.AddSeconds(-secondsAgo), Now, TimeZoneInfo.Utc).ShouldBe(expected);

    [Fact]
    public void Today_is_the_local_day()
    {
        var plusTen = TimeZoneInfo.CreateCustomTimeZone("UTC+10", TimeSpan.FromHours(10), "UTC+10", "UTC+10");

        // 13:30 UTC on the 9th is 23:30 there; 15:30 UTC is already 01:30 on the 10th.
        Ago.Describe(Now.AddHours(-2), Now, plusTen).ShouldBe("9 Oct at 23:30");
    }
}
