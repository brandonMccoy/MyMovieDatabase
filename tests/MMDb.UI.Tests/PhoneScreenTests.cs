using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.Contracts.Movies;
using MMDb.Contracts;
using MMDb.Contracts.Phone;
using MMDb.Testing;
using MMDb.UI.Layout;
using MMDb.UI.Pages;
using MMDb.UI.Scanning;
using static MMDb.UI.Tests.FilterPanel;

namespace MMDb.UI.Tests;

/// <summary>The phone's frame: pairing first, then the screens with the bottom bar; the PC being away takes over.</summary>
public sealed class PhoneLayoutTests : PhoneTestContext
{
    private IRenderedComponent<PhoneLayout> RenderApp<TPage>(string location = "")
        where TPage : IComponent
    {
        Navigation.NavigateTo(location);
        return Render<PhoneLayout>(p => p.Add(x => x.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<TPage>(0);
            b.CloseComponent();
        })));
    }

    [Fact]
    public async Task A_paired_phone_shows_the_library_with_the_bottom_bar()
    {
        await SeedAsync(Heat);

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".card");
        app.Find(".appbar .ttl").TextContent.ShouldBe("Movies");
        app.FindAll(".bnav a").Select(a => (a.GetAttribute("href"), Text(a), a.ClassList.Contains("on"))).ShouldBe(
        [
            ("", "Movies", true),
            ("comics", "Comics", false),
            ("scan", "", false),
            ("wishlist", "Wishlist", false),
            ("stats", "Stats", false),
        ]);
        app.Find(".bnav a.scan").GetAttribute("aria-label").ShouldBe("Scan");
        Sync.Syncs.ShouldBe(1, "opening the app brings the copy up to date");
    }

    [Fact]
    public void The_bottom_bar_marks_comics_when_there()
    {
        var app = RenderApp<ComicLibrary>("comics");

        app.WaitForElement(".appbar");
        app.FindAll(".bnav a.on").ShouldHaveSingleItem().TextContent.Trim().ShouldBe("Comics");
    }

    [Fact]
    public void The_bottom_bar_marks_the_wishlist_when_there()
    {
        var app = RenderApp<WishlistPage>("wishlist");

        app.WaitForElement(".appbar");
        app.FindAll(".bnav a.on").ShouldHaveSingleItem().TextContent.Trim().ShouldBe("Wishlist");
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan/review")]
    [InlineData("movies/new")]
    [InlineData("comics/new")]
    [InlineData("wishlist/movies/new")]
    [InlineData("wishlist/comics/new")]
    public void Scanning_and_reviewing_fill_the_screen_without_the_bottom_bar(string location)
    {
        var app = RenderApp<PhoneLink>(location);

        app.WaitForElement(".phone-body");
        app.FindAll(".bnav").ShouldBeEmpty();
    }

    [Fact]
    public void A_phone_that_isnt_paired_gets_the_pairing_screen_and_nothing_else()
    {
        Link.Set(PcLinkState.NotPaired);

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".pair-screen");
        app.Find("h1").TextContent.ShouldBe("Pair with your PC");
        app.FindAll(".bnav, .appbar, .notice").ShouldBeEmpty();
    }

    [Fact]
    public void A_phone_unpaired_on_the_pc_is_told_so_and_asked_to_pair_again()
    {
        Link.Set(PcLinkState.Unpaired);

        var app = RenderApp<MovieLibrary>();

        Text(app.WaitForElement(".pair-screen .notice")).ShouldBe("This phone was unpaired on DESK. Pair it again to carry on.");
    }

    [Fact]
    public async Task Away_from_the_pc_the_phone_shows_its_copy_and_says_how_fresh_it_is()
    {
        await SeedAsync(Heat);
        Clock.SetUtcNow(FakePhoneSync.Now);
        Link.PcUp = false;

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".card").QuerySelector(".t")!.TextContent.ShouldBe("Heat");
        Text(app.WaitForElement(".away")).ShouldBe("Away from your PC · collection as of today 8:12 AM");
        app.FindAll(".bnav").ShouldHaveSingleItem();
        app.FindAll(".phone-notice").ShouldBeEmpty();

        Link.PcUp = true;
        await app.InvokeAsync(() => Sync.SyncAsync(Ct));

        app.WaitForAssertion(() => app.FindAll(".away").ShouldBeEmpty());
    }

    [Theory]
    [InlineData(1, "today 8:12 AM")]
    [InlineData(20, "yesterday 1:12 PM")]
    [InlineData(24 * 9, "1 Oct")]
    [InlineData(24 * 300, "14 Dec 2025")]
    public void An_older_copy_is_dated(int hoursAgo, string expected)
    {
        Clock.SetUtcNow(FakePhoneSync.Now);
        Sync.Set(Sync.Status with { SyncedAt = FakePhoneSync.Now.AddHours(-hoursAgo) });
        Link.PcUp = false;

        var app = RenderApp<ComicLibrary>("comics");

        Text(app.WaitForElement(".away")).ShouldBe($"Away from your PC · collection as of {expected}");
    }

    [Fact]
    public void What_was_saved_away_from_home_is_counted_until_it_is_sent()
    {
        Link.PcUp = false;
        Sync.Set(Sync.Status with { Waiting = 3 });

        var app = RenderApp<WishlistPage>("wishlist");

        Text(app.WaitForElement(".away")).ShouldBe("Away from your PC · 3 changes to send");
        app.Find(".away b").TextContent.ShouldBe("3 changes");

        Sync.Set(Sync.Status with { Waiting = 1 });

        app.WaitForAssertion(() => Text(app.Find(".away")).ShouldBe("Away from your PC · 1 change to send"));
    }

    [Fact]
    public void At_home_with_nothing_waiting_there_is_no_line()
    {
        Sync.Set(Sync.Status with { Waiting = 2 });

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".appbar");
        app.FindAll(".away").ShouldBeEmpty("at home what is waiting is already on its way");
    }

    [Fact]
    public void What_the_pc_wouldnt_take_is_said_until_it_is_dismissed()
    {
        Sync.Set(Sync.Status with { TurnedDown = ["Dune (2021) on the wishlist", "Saga #9 on the wishlist"] });

        var app = RenderApp<WishlistPage>("wishlist");

        Text(app.WaitForElement(".away.turned-down")).ShouldBe("2 changes couldn't be sent: Dune (2021) on the wishlist; Saga #9 on the wishlist OK");

        app.Find(".away.turned-down button").Click();

        app.WaitForAssertion(() => app.FindAll(".away").ShouldBeEmpty());
    }

    [Fact]
    public void A_phone_and_pc_on_different_versions_say_so_over_the_copy()
    {
        Sync.Outcome = PhoneSyncState.WrongVersion;

        var app = RenderApp<MovieLibrary>();

        app.WaitForAssertion(() => Text(app.Find(".away")).ShouldBe("Shelf on this phone and on DESK are different versions. Update both to sync."));
        app.FindAll(".bnav").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_phone_that_never_had_the_collection_needs_its_pc_once()
    {
        await SeedAsync(Heat);
        Sync.Set(new(PhoneSyncState.Idle, SyncedAt: null, Waiting: 0, TurnedDown: []));
        Link.PcUp = false;

        var app = RenderApp<MovieLibrary>();

        app.WaitForElement(".phone-notice");
        app.Find("h1").TextContent.ShouldBe("Can't reach DESK");
        Text(app.Find(".phone-notice .small")).ShouldBe("Last seen at 192.168.1.24:5243");
        app.FindAll(".bnav, .card").ShouldBeEmpty();

        Link.PcUp = true;
        app.Find(".phone-notice button.primary").Click();

        app.WaitForElement(".card").QuerySelector(".t")!.TextContent.ShouldBe("Heat");
        app.FindAll(".bnav").ShouldHaveSingleItem();
    }

    [Fact]
    public void Trying_again_while_the_pc_is_still_off_stays_put()
    {
        Sync.Set(new(PhoneSyncState.Idle, SyncedAt: null, Waiting: 0, TurnedDown: []));
        Link.PcUp = false;
        var app = RenderApp<MovieLibrary>();
        app.WaitForElement(".phone-notice");

        app.Find(".phone-notice button.primary").Click();

        app.WaitForAssertion(() => Sync.Syncs.ShouldBe(2));
        app.Find("h1").TextContent.ShouldBe("Can't reach DESK");
        app.Find(".phone-notice a.link-btn").GetAttribute("href").ShouldBe("phone");
    }

    [Fact]
    public void While_the_collection_is_first_copied_the_phone_says_so()
    {
        Sync.Set(new(PhoneSyncState.Idle, SyncedAt: null, Waiting: 0, TurnedDown: []));
        Sync.Hold = new();

        var app = RenderApp<MovieLibrary>();

        app.WaitForAssertion(() => app.Find(".phone-notice h1").TextContent.ShouldBe("Copying your collection"));
        Text(app.Find(".phone-notice p")).ShouldStartWith("From DESK, so this phone has it wherever you are.");
        app.FindAll(".bnav, .appbar").ShouldBeEmpty();

        Sync.Hold.SetResult();

        app.WaitForElement(".appbar");
        app.FindAll(".phone-notice").ShouldBeEmpty();
    }

    [Fact]
    public void A_phone_that_cant_copy_from_another_version_of_shelf_says_to_update()
    {
        Sync.Set(new(PhoneSyncState.Idle, SyncedAt: null, Waiting: 0, TurnedDown: []));
        Sync.Outcome = PhoneSyncState.WrongVersion;

        var app = RenderApp<MovieLibrary>();

        app.WaitForAssertion(() => app.Find(".phone-notice h1").TextContent.ShouldBe("Update Shelf"));
        Text(app.Find(".phone-notice p")).ShouldBe("Shelf on this phone and on DESK are different versions. Update both, then try again.");
    }

    [Fact]
    public async Task Stats_is_the_fifth_tab_and_counts_the_phones_copy()
    {
        await SeedAsync(Heat);

        var app = RenderApp<StatsPage>("stats");

        app.WaitForElement(".tiles");
        app.Find(".appbar .ttl").TextContent.ShouldBe("Stats");
        app.FindAll(".bnav a.on").ShouldHaveSingleItem().TextContent.Trim().ShouldBe("Stats");
        Text(app.Find(".tile")).ShouldStartWith("Films1");
        app.FindAll(".topbar, .section-head").ShouldBeEmpty();
        app.Markup.ShouldNotContain("on this PC");
    }

    [Fact]
    public void This_phone_says_how_fresh_the_copy_is_and_syncs_when_asked()
    {
        Clock.SetUtcNow(FakePhoneSync.Now);
        Link.PcUp = false;
        Sync.Set(Sync.Status with { Waiting = 2 });
        var app = RenderApp<PhoneLink>("phone");
        app.WaitForElement("#copy-heading");

        Text(app.Find("section[aria-labelledby='copy-heading'] .desc")).ShouldBe("As of today 8:12 AM · 2 changes to send");

        Link.PcUp = true;
        Sync.Hold = new();
        var before = Sync.Syncs;
        app.Find("section[aria-labelledby='copy-heading'] button").Click();

        app.WaitForAssertion(() => Text(app.Find("section[aria-labelledby='copy-heading'] button")).ShouldBe("Syncing…"));
        app.Find("section[aria-labelledby='copy-heading'] button").HasAttribute("disabled").ShouldBeTrue();

        Sync.Hold.SetResult();

        app.WaitForAssertion(() => Text(app.Find("section[aria-labelledby='copy-heading'] .desc")).ShouldBe("As of today 9:12 AM"));
        Text(app.Find("section[aria-labelledby='copy-heading'] button")).ShouldBe("Sync now");
        Sync.Syncs.ShouldBe(before + 1);
    }

    [Fact]
    public void The_pc_can_be_forgotten_even_while_it_is_away()
    {
        Link.PcUp = false;
        var app = RenderApp<PhoneLink>("phone");

        app.WaitForElement(".set");
        Text(app.Find(".set h3")).ShouldBe("Paired with DESK");
        Text(app.Find(".set .desc")).ShouldBe("Not reachable right now · 192.168.1.24:5243");

        app.Find(".set button").Click();
        app.Find(".confirm").TextContent.ShouldContain("Forget DESK?");
        app.Find(".confirm button.danger").Click();

        app.WaitForElement(".pair-screen");
        Link.State.ShouldBe(PcLinkState.NotPaired);
    }

    [Fact]
    public void Forgetting_the_pc_can_be_cancelled()
    {
        var app = RenderApp<PhoneLink>("phone");
        app.WaitForElement(".set");
        Text(app.Find(".set .desc")).ShouldBe("Connected · 192.168.1.24:5243");

        app.Find(".set button").Click();
        app.Find(".confirm-actions button:not(.danger)").Click();

        app.FindAll(".confirm").ShouldBeEmpty();
        Link.State.ShouldBe(PcLinkState.Connected);
    }
}

/// <summary>Pairing on the phone: scanning the PC's QR code, or typing its address and code.</summary>
public sealed class PhonePairingScreenTests : PhoneTestContext
{
    private IRenderedComponent<PhoneLayout> RenderUnpaired()
    {
        Link.Set(PcLinkState.NotPaired);
        Link.PcName = null;
        var app = Render<PhoneLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"the-app\">in</div>"))));
        app.WaitForElement(".pair-screen");
        return app;
    }

    [Fact]
    public void Scanning_the_code_on_the_pc_pairs_the_phone_and_lets_it_in()
    {
        var app = RenderUnpaired();

        app.Find("button.scan-code").Click();

        app.WaitForElement(".camera.pairing");
        Scanner.Running.ShouldBe(ScanTarget.PairingCode);
        Text(app.Find(".cam-hint")).ShouldBe("Point at the code in Shelf on your PCSettings → Phone");

        Scanner.Read("shelf://pair?h=192.168.1.24&p=5243&c=482913&f=AB");

        app.WaitForElement(".the-app");
        Link.PairAttempts.ShouldBe(["shelf://pair?h=192.168.1.24&p=5243&c=482913&f=AB"]);
        Scanner.Running.ShouldBeNull();
    }

    [Fact]
    public void The_camera_reading_the_code_many_times_pairs_once()
    {
        Link.PairAnswer = PairStatus.WrongCode;
        var app = RenderUnpaired();
        app.Find("button.scan-code").Click();
        app.WaitForElement(".camera");

        Scanner.Read("shelf://pair?c=1");
        app.WaitForElement(".problem");

        Link.PairAttempts.Count.ShouldBe(1);
        Scanner.Running.ShouldBeNull("it stops at the first code");
        Should.Throw<InvalidOperationException>(() => Scanner.Read("shelf://pair?c=1"));
    }

    [Theory]
    [InlineData(PairStatus.NotACode, "That isn't a Shelf pairing code. Use the one under Settings → Phone on the PC.")]
    [InlineData(PairStatus.WrongCode, "The PC turned the code down. It may have expired: use the code the PC shows now.")]
    [InlineData(PairStatus.WrongPc, "A different computer answered at that address. Scan the code on the PC again.")]
    [InlineData(PairStatus.Unreachable, "No PC answered. Check that Shelf is open on the PC and this phone is on the same Wi-Fi.")]
    public void Pairing_that_doesnt_work_says_why_and_can_be_tried_again(PairStatus answer, string message)
    {
        Link.PairAnswer = answer;
        var app = RenderUnpaired();
        app.Find("#pc-address").Input("192.168.1.24");
        app.Find("#pc-code").Input("482 913");

        app.Find("form.pair-typed").Submit();

        Text(app.WaitForElement(".problem")).ShouldBe(message);
        app.FindAll(".the-app").ShouldBeEmpty();

        Link.PairAnswer = PairStatus.Paired;
        app.Find("form.pair-typed").Submit();
        app.WaitForElement(".the-app");
        Link.PairAttempts.ShouldBe(["192.168.1.24|482 913", "192.168.1.24|482 913"]);
    }

    [Fact]
    public void Typing_needs_both_the_address_and_the_code()
    {
        var app = RenderUnpaired();
        var pair = () => app.Find("form.pair-typed button[type=submit]");

        pair().HasAttribute("disabled").ShouldBeTrue();
        app.Find("#pc-address").Input("192.168.1.24");
        pair().HasAttribute("disabled").ShouldBeTrue();
        app.Find("#pc-code").Input("482913");
        pair().HasAttribute("disabled").ShouldBeFalse();
    }

    [Theory]
    [InlineData(ScannerStart.NotAllowed, "Shelf isn't allowed to use the camera. Allow it in Android's settings, or type the address and code instead.")]
    [InlineData(ScannerStart.NoCamera, "This phone has no camera Shelf can use. Type the address and code instead.")]
    public void Without_the_camera_the_code_can_still_be_typed(ScannerStart answer, string message)
    {
        Scanner.Answer = answer;
        var app = RenderUnpaired();

        app.Find("button.scan-code").Click();

        Text(app.WaitForElement(".problem")).ShouldBe(message);
        app.FindAll(".camera").ShouldBeEmpty();
        app.FindAll("form.pair-typed").ShouldHaveSingleItem();
    }

    [Fact]
    public void Scanning_can_be_stopped()
    {
        var app = RenderUnpaired();
        app.Find("button.scan-code").Click();
        app.WaitForElement(".camera");

        app.Find(".cam-bar button").Click();

        app.WaitForElement(".pair-screen");
        Scanner.Running.ShouldBeNull();
        Link.PairAttempts.ShouldBeEmpty();
    }
}

/// <summary>Browsing on the phone: the libraries as mocked, and pages that open but don't edit.</summary>
public sealed class PhoneBrowseTests : PhoneTestContext
{
    [Fact]
    public async Task The_movie_library_is_a_search_a_filter_button_and_covers()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        Navigation.NavigateTo("");

        var page = Render<MovieLibrary>();

        page.WaitForElement(".card");
        page.Find(".appbar .ttl").TextContent.ShouldBe("Movies");
        page.Find(".appbar a.right").GetAttribute("href").ShouldBe("phone");
        page.Find(".search input").GetAttribute("placeholder").ShouldBe("Do I own…?");
        Chips(page).ShouldBe(["Filter"]);
        page.FindAll(".filters select, .section-head, a[href='movies/new'], .sheet").ShouldBeEmpty();
        Titles(page).ShouldBe(["Alien", "Heat"]);
    }

    private static string[] Titles(IRenderedComponent<MovieLibrary> page) => [.. page.FindAll(".card .t").Select(t => t.TextContent)];

    /// <summary>Heat (1995, crime drama, 4K and DVD), Alien (1979, horror, Blu-ray) and Arrival (2016, drama, digital), with the library open.</summary>
    private async Task<IRenderedComponent<MovieLibrary>> LibraryOfThreeAsync(string address = "")
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979, MovieFormatKind.BluRay) with { Genres = ["Horror"] });
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.Digital) with { Genres = ["Drama"] });
        Navigation.NavigateTo(address);
        var page = Render<MovieLibrary>();
        page.WaitForElement(".filters");
        return page;
    }

    [Fact]
    public async Task The_filter_button_opens_a_panel_with_a_tick_box_for_each_value_of_each_filter()
    {
        var page = await LibraryOfThreeAsync();

        page.Find(".open-filters").Click();

        var sheet = page.Find(".sheet");
        sheet.GetAttribute("role").ShouldBe("dialog");
        sheet.GetAttribute("aria-label").ShouldBe("Filters");
        Boxes(page).ShouldBe([("Format", "4K UHD 1, Blu-ray 1, DVD 1, Digital 1"), ("Genre", "Crime, Drama, Horror"), ("Decade", "2010s, 1990s, 1970s")]);
        page.FindAll(".filter-pop, .pop-shade").ShouldBeEmpty("the dropdown panel is the PC's");
        sheet.QuerySelectorAll("input[type=checkbox]").Length.ShouldBe(10);
        Ticked(page).ShouldBeEmpty();
        sheet.QuerySelectorAll(".sheet-foot button").Select(b => b.TextContent).ShouldBe(["Clear", "Apply Filter"]);
    }

    [Fact]
    public async Task Ticks_filter_nothing_until_apply_filter()
    {
        var page = await LibraryOfThreeAsync();
        page.Find(".open-filters").Click();

        Tick(page, "Blu-ray");
        Tick(page, "Digital");

        RelativeLocation.ShouldBe("");
        Titles(page).Length.ShouldBe(3);
        Chips(page).ShouldBe(["Filter"]);

        Apply(page);

        RelativeLocation.ShouldBe("?format=BluRay&format=Digital");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Alien", "Arrival"], ignoreOrder: true));
        page.FindAll(".sheet").ShouldBeEmpty();
        Chips(page).ShouldBe(["Filter", "Blu-ray", "Digital"]);
    }

    [Fact]
    public async Task Ticks_in_one_filter_widen_it_and_the_other_filters_narrow_it()
    {
        var page = await LibraryOfThreeAsync();
        page.Find(".open-filters").Click();

        Tick(page, "2010s");
        Tick(page, "Drama");
        Tick(page, "Digital");
        Tick(page, "DVD");
        Apply(page);

        RelativeLocation.ShouldBe("?format=Dvd&format=Digital&genre=Drama&decade=2010");
        page.WaitForAssertion(() => Titles(page).ShouldBe(["Arrival"]));
        Chips(page).ShouldBe(["Filter", "DVD", "Digital", "Drama", "2010s"]);
    }

    [Fact]
    public async Task The_panel_opens_on_what_is_applied_and_closing_it_changes_nothing()
    {
        var page = await LibraryOfThreeAsync("?format=Dvd&decade=1990");
        page.Find(".open-filters").Click();
        Ticked(page).ShouldBe(["DVD", "1990s"]);

        Tick(page, "Blu-ray");
        Tick(page, "DVD", on: false);
        page.Find(".sheet button[aria-label='Close']").Click();

        page.FindAll(".sheet").ShouldBeEmpty();
        RelativeLocation.ShouldBe("?format=Dvd&decade=1990");
        Titles(page).ShouldBe(["Heat"]);

        page.Find(".open-filters").Click();
        Ticked(page).ShouldBe(["DVD", "1990s"], "the ticks that weren't applied are forgotten");

        page.Find(".sheet-shade").Click();

        page.FindAll(".sheet, .sheet-shade").ShouldBeEmpty("a tap outside the panel closes it too");
        RelativeLocation.ShouldBe("?format=Dvd&decade=1990");
    }

    [Fact]
    public async Task Unticking_everything_and_applying_takes_the_filters_off()
    {
        var page = await LibraryOfThreeAsync("?format=Dvd&genre=Crime");
        page.Find(".open-filters").Click();

        Tick(page, "DVD", on: false);
        Tick(page, "Crime", on: false);
        Apply(page);

        RelativeLocation.ShouldBe("");
        page.WaitForAssertion(() => Titles(page).Length.ShouldBe(3));
        Chips(page).ShouldBe(["Filter"]);
    }

    [Fact]
    public async Task A_chip_takes_its_own_filter_off()
    {
        var page = await LibraryOfThreeAsync("?format=BluRay&format=Dvd&genre=Horror&decade=1970");
        Chips(page).ShouldBe(["Filter", "Blu-ray", "DVD", "Horror", "1970s"]);
        Titles(page).ShouldBe(["Alien"]);

        var dvd = page.FindAll(".filters a.chip").Single(c => Text(c) == "DVD");
        dvd.GetAttribute("aria-label").ShouldBe("DVD: clear");
        Navigation.NavigateTo(dvd.GetAttribute("href")!);

        RelativeLocation.ShouldBe("?format=BluRay&genre=Horror&decade=1970");
        page.WaitForAssertion(() => Chips(page).ShouldBe(["Filter", "Blu-ray", "Horror", "1970s"]));

        Navigation.NavigateTo(page.FindAll(".filters a.chip").Single(c => Text(c) == "1970s").GetAttribute("href")!);

        RelativeLocation.ShouldBe("?format=BluRay&genre=Horror");
        page.WaitForAssertion(() => Chips(page).ShouldBe(["Filter", "Blu-ray", "Horror"]));
    }

    [Fact]
    public async Task Clear_in_the_panel_takes_every_filter_off_at_once()
    {
        var page = await LibraryOfThreeAsync("?format=Dvd&genre=Crime&decade=1990&director=Michael%20Mann");
        Chips(page).ShouldBe(["Filter", "DVD", "Crime", "1990s", "Directed by Michael Mann"]);
        page.Find(".open-filters").Click();

        Clear(page);

        RelativeLocation.ShouldBe("");
        page.FindAll(".sheet").ShouldBeEmpty();
        page.WaitForAssertion(() => Titles(page).Length.ShouldBe(3));
        Chips(page).ShouldBe(["Filter"]);
    }

    [Fact]
    public async Task Filters_that_match_nothing_keep_the_filter_row_to_change_them()
    {
        var page = await LibraryOfThreeAsync("?format=Digital&genre=Horror");

        page.WaitForAssertion(() => page.Find(".empty h2").TextContent.ShouldBe("No films match"));
        Chips(page).ShouldBe(["Filter", "Digital", "Horror"]);
    }

    [Fact]
    public async Task Do_i_own_it_searches_as_you_type()
    {
        await SeedAsync(Heat);
        await SeedAsync(Film("Alien", 1979));
        Navigation.NavigateTo("");
        var page = Render<MovieLibrary>();
        page.WaitForElement(".card");

        page.Find(".search input").Input("hea");

        RelativeLocation.ShouldBe("?q=hea");
    }

    [Fact]
    public async Task A_film_added_on_the_phone_can_be_taken_back_until_it_is_sent()
    {
        var sent = await SeedAsync(Heat);
        var unsent = await SeedAsync(Film("Alien", 1979, MovieFormatKind.Dvd));
        Sync.Unsent(unsent.Id);

        var theirs = Render<MovieDetail>(p => p.Add(x => x.Id, sent.Id));
        theirs.WaitForElement(".actions");
        theirs.FindAll(".actions button").ShouldBeEmpty("what the PC has is removed on the PC");

        var mine = Render<MovieDetail>(p => p.Add(x => x.Id, unsent.Id));
        Text(mine.WaitForElement(".actions button")).ShouldBe("Remove");
        mine.Find(".actions button").Click();
        Text(mine.Find(".confirm")).ShouldBe("Remove Alien? It hasn't been sent to your PC yet, so this takes it back.Cancel Remove");
        mine.Find(".confirm button.danger").Click();

        mine.WaitForAssertion(() => RelativeLocation.ShouldBe(""));
        (await Api.GetMovieAsync(unsent.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_comic_added_on_the_phone_can_be_taken_back_until_it_is_sent()
    {
        var sent = await SeedComicAsync(TestComics.Saga7);
        var unsent = await SeedComicAsync(TestComics.Daredevil181);
        Sync.Unsent(unsent.Id);

        var theirs = Render<ComicDetail>(p => p.Add(x => x.Id, sent.Id));
        theirs.WaitForElement(".detail, .cover, h1");
        theirs.FindAll(".actions button").ShouldBeEmpty();

        var mine = Render<ComicDetail>(p => p.Add(x => x.Id, unsent.Id));
        Text(mine.WaitForElement(".actions button")).ShouldBe("Remove");
        mine.Find(".actions button").Click();
        mine.Find(".confirm").TextContent.ShouldContain("It hasn't been sent to your PC yet");
        mine.Find(".confirm button.danger").Click();

        mine.WaitForAssertion(() => RelativeLocation.ShouldBe("comics"));
        (await Api.GetComicAsync(unsent.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public void An_empty_shelf_on_the_phone_points_at_the_scanner()
    {
        Navigation.NavigateTo("");
        var movies = Render<MovieLibrary>();
        movies.WaitForElement(".empty");
        movies.Find(".empty a.btn").GetAttribute("href").ShouldBe("scan");
        Text(movies.Find(".empty p")).ShouldBe("Scan the barcode of the first movie you own.");

        Navigation.NavigateTo("comics");
        var comics = Render<ComicLibrary>();
        comics.WaitForElement(".empty");
        comics.Find(".empty a.btn").GetAttribute("href").ShouldBe("scan");
    }

    [Fact]
    public async Task The_comic_library_keeps_its_shelves_and_drops_the_pc_only_controls()
    {
        await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo("comics");

        var page = Render<ComicLibrary>();

        page.WaitForElement(".series");
        page.Find(".appbar .ttl").TextContent.ShouldBe("Comics");
        page.Find(".search input").GetAttribute("placeholder").ShouldBe("Do I own…?");
        page.FindAll(".filters .chip").Select(c => Text(c)).ShouldBe(["Series", "Issues", "Trades & HCs", "Key issues0"]);
        page.FindAll(".filters select, .section-head, a[href='comics/new']").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_film_opens_to_read_with_its_trailer_but_editing_stays_on_the_pc()
    {
        var heat = await SeedAsync(Heat);
        Navigation.NavigateTo($"movies/{heat.Id}");

        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));

        page.WaitForElement("h1.title-lg");
        Text(page.Find(".actions")).ShouldBe("Watch trailer");
        page.FindAll("a[href$='/edit'], .menu-wrap, .topbar .search").ShouldBeEmpty();
        page.FindAll(".fmt").Select(f => f.TextContent).ShouldContain("4K UHD");
    }

    [Fact]
    public async Task A_comic_opens_to_read_but_editing_stays_on_the_pc()
    {
        var saga = await SeedComicAsync(TestComics.Saga7);
        Navigation.NavigateTo($"comics/{saga.Id}");

        var page = Render<ComicDetail>(p => p.Add(x => x.Id, saga.Id));

        page.WaitForElement("h1");
        page.FindAll(".actions, .menu-wrap, a[href$='/edit'], .topbar .search").ShouldBeEmpty();
        page.Markup.ShouldContain("Brian K. Vaughan");
    }

    [Fact]
    public void A_missing_key_is_to_be_added_in_the_pcs_settings()
    {
        var notice = Render<MMDb.UI.Components.LookupProblem>(p => p.Add(x => x.Status, LookupStatus.NotConfigured));

        Text(notice.Find(".notice")).ShouldBe("TMDB isn't set up on this PC yet. Add your TMDB key in Settings on the PC.");
        notice.FindAll("a").ShouldBeEmpty();
    }
}
