using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.Application.Metadata;
using MMDb.Contracts.Digital;
using MMDb.Contracts.Movies;
using MMDb.Domain.Movies;
using MMDb.Testing;
using MMDb.UI.Layout;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Settings → Digital movies, the "new files" line in the sidebar, the import screen, and Play file on a film's page.</summary>
public sealed class DigitalImportScreenTests : ShelfTestContext
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly MovieDetails Arrival = TestMovies.Titled("Arrival", 2016) with { TmdbId = 329865, PosterPath = "/arrival.jpg" };

    public DigitalImportScreenTests() =>
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb).Add(TestMovies.HeatAndDustOnTmdb).Add(Arrival);

    private string Folder => MoviesFolder;

    private string InFolder(string name) => Path.Combine(Folder, name);

    /// <summary>Puts empty files in the movie folder and returns their full paths.</summary>
    private string[] Put(params string[] names) => [.. names.Select(name =>
    {
        var path = InFolder(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    })];

    private async Task UseFolderAsync() => (await Digital.SetFolderAsync(Folder, Ct)).ShouldBeTrue();

    /// <summary>The import screen once every file has been looked up.</summary>
    private async Task<IRenderedComponent<DigitalImport>> OpenImportAsync(params string[] names)
    {
        Put(names);
        await UseFolderAsync();
        var page = Render<DigitalImport>();
        page.WaitForAssertion(
            () =>
            {
                Digital.Folder.Progress.ShouldBeNull();
                Digital.Folder.Files.ShouldAllBe(f => f.Match != FileMatch.Waiting || Digital.Folder.Problem != null);
                page.FindAll(".imp-list .imp").Count.ShouldBe(Digital.Folder.Files.Count);
                page.FindAll(".imp-list .imp .small").Select(s => s.TextContent).ShouldNotContain("Looking up…");
            },
            Patience);
        return page;
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

    private static string Text(IElement element) => string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Each waiting file as its row reads: file, "Title | under the title", state, first button, ticked.</summary>
    private static (string File, string Film, string State, string Action, bool Ticked)[] Rows(IRenderedComponent<DigitalImport> page) =>
        [.. page.FindAll(".imp-list .imp").Select(row => (
            row.QuerySelector(".file")!.TextContent,
            row.QuerySelector(".ht") is { } film ? $"{film.ChildNodes[0].TextContent} | {film.QuerySelector("span")!.TextContent}" : Text(row.QuerySelector(".hit")!),
            row.QuerySelector(".conf")!.TextContent,
            row.QuerySelector(".imp-actions .btn")!.TextContent.Trim(),
            row.QuerySelector(".cb")!.ClassList.Contains("on")))];

    private static IElement Row(IRenderedComponent<DigitalImport> page, string file) =>
        page.FindAll(".imp-list .imp").Single(row => row.QuerySelector(".file")!.TextContent == file);

    // The screens redraw by themselves (the folder is scanned and looked up in the background), so a click
    // is made on the renderer's own thread, where nothing can redraw between finding the element and clicking it.
    private static Task Click<T>(IRenderedComponent<T> page, Func<IElement> element)
        where T : IComponent => page.InvokeAsync(() => element().Click());

    private static IElement Chip(IRenderedComponent<DigitalImport> page, string name) =>
        page.FindAll(".filters .chip").Single(c => c.ChildNodes[0].TextContent == name);

    /// <summary>The chips with their counts: ("Needs review", "2").</summary>
    private static (string Name, string? Count)[] Chips(IRenderedComponent<DigitalImport> page) =>
        [.. page.FindAll(".filters .chip").Select(c => (c.ChildNodes[0].TextContent, c.QuerySelector(".n")?.TextContent))];

    private async Task<MovieDto> FilmAsync(Guid id) => (await Api.GetMovieAsync(id, Ct)).ShouldNotBeNull();

    private async Task<MovieDto> OnlyFilmAsync() =>
        await FilmAsync((await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().Id);

    // ---- Settings ----

    [Fact]
    public void Before_a_movie_folder_is_chosen_settings_offers_to_choose_one()
    {
        var page = Render<Settings>();

        var section = page.Find("section[aria-labelledby=digital-heading]");
        section.QuerySelector("h3")!.TextContent.ShouldBe("Digital movies");
        section.QuerySelector(".folder")!.TextContent.ShouldBe("Not set");
        section.QuerySelector("button.choose-movies")!.TextContent.Trim().ShouldBe("Choose folder");
        section.QuerySelectorAll(".switch-ui").ShouldBeEmpty();
        section.QuerySelectorAll("a.review-files").ShouldBeEmpty();
    }

    [Fact]
    public async Task Choosing_the_movie_folder_shows_what_is_in_it_without_asking_tmdb()
    {
        Put("Heat.1995.1080p.mkv", "arrival_2016.mp4", "notes.txt");
        FolderPicker.Answer = Folder;
        var page = Render<Settings>();

        await Click(page, () => page.Find("button.choose-movies"));

        page.WaitForAssertion(() => page.Find(".files-state").TextContent.ShouldBe("2 video files · 2 new"), Patience);
        var section = page.Find("section[aria-labelledby=digital-heading]");
        section.QuerySelector(".folder")!.TextContent.ShouldBe(Folder);
        section.QuerySelector("button.choose-movies")!.TextContent.Trim().ShouldBe("Change");
        section.QuerySelector(".switch-ui")!.GetAttribute("aria-checked").ShouldBe("true");
        section.QuerySelector("a.review-files")!.GetAttribute("href").ShouldBe("import");
        Online.Tmdb.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_chosen_movie_folder_that_has_gone_is_turned_down()
    {
        var gone = Path.Combine(Folder, "gone");
        FolderPicker.Answer = gone;
        var page = Render<Settings>();

        await Click(page, () => page.Find("button.choose-movies"));

        page.WaitForAssertion(
            () => page.Find("section[aria-labelledby=digital-heading] .warn").TextContent.ShouldBe($"{gone} isn't there any more. Choose another folder."),
            Patience);
        page.Find("section[aria-labelledby=digital-heading] .folder").TextContent.ShouldBe("Not set");
    }

    [Fact]
    public async Task The_watch_switch_stops_and_starts_watching()
    {
        await UseFolderAsync();
        var page = Render<Settings>();
        Online.Watcher.Watched.ShouldBe([Folder]);

        await Click(page, () => page.Find(".switch-ui"));

        page.WaitForAssertion(() => page.Find(".switch-ui").GetAttribute("aria-checked").ShouldBe("false"), Patience);
        page.WaitForAssertion(() => Online.Watcher.Watched.ShouldBeEmpty(), Patience);

        await Click(page, () => page.Find(".switch-ui"));

        page.WaitForAssertion(() => page.Find(".switch-ui").GetAttribute("aria-checked").ShouldBe("true"), Patience);
        page.WaitForAssertion(() => Online.Watcher.Watched.ShouldBe([Folder]), Patience);
    }

    [Fact]
    public async Task Settings_says_when_the_movie_folder_cannot_be_found()
    {
        using var unplugged = new TempFolder();
        (await Digital.SetFolderAsync(unplugged.Path, Ct)).ShouldBeTrue();
        Directory.Delete(unplugged.Path);
        await Digital.RescanAsync(Ct);

        var page = Render<Settings>();

        page.Find("section[aria-labelledby=digital-heading] .warn").TextContent.ShouldBe("Folder not found. Reconnect its drive, or choose the folder again.");
        page.FindAll("a.review-files").ShouldBeEmpty();
    }

    // ---- Sidebar ----

    [Fact]
    public async Task The_sidebar_says_how_many_new_files_wait_and_follows_the_watcher()
    {
        var layout = RenderLayout();
        layout.FindAll(".new-files").ShouldBeEmpty();

        Put("Heat.1995.1080p.mkv");
        await UseFolderAsync();

        layout.WaitForAssertion(() => Text(layout.Find(".new-files")).ShouldBe("1 new file"), Patience);
        layout.Find(".new-files").GetAttribute("href").ShouldBe("import");

        Put("arrival_2016.mp4", "heat.mkv");
        Online.Watcher.Stir();

        layout.WaitForAssertion(() => Text(layout.Find(".new-files")).ShouldBe("3 new files"), Patience);
        Online.Tmdb.Searches.ShouldBeEmpty();
    }

    // ---- Import screen ----

    [Fact]
    public void Without_a_folder_the_import_screen_explains_and_offers_to_choose_one()
    {
        var page = Render<DigitalImport>();

        page.Find("h1").TextContent.ShouldBe("Import digital movies");
        page.Find(".import-setup .folder").TextContent.ShouldBe("Not set");
        page.Find(".import-intro").TextContent.ShouldContain("Choose the folder where your movie files are.");
        page.Find("button.add").HasAttribute("disabled").ShouldBeTrue();
        page.FindAll(".import-sum").ShouldBeEmpty();
        page.Find(".topbar .back").GetAttribute("href").ShouldBe("settings");
    }

    [Fact]
    public async Task Each_file_shows_its_match_and_clear_matches_are_ticked()
    {
        await SeedAsync(Film("Arrival", 2016, MovieFormatKind.BluRay, MovieFormatKind.Dvd) with { TmdbId = 329865 });

        var page = await OpenImportAsync("Heat.1995.1080p.BluRay.mkv", "heat.mkv", "arrival_2016.mp4", "movie_final_v2.mp4");

        Rows(page).ShouldBe(
        [
            ("arrival_2016.mp4", "Arrival | 2016 · adds Digital to your Blu-ray and DVD", "Matched", "Change", true),
            ("Heat.1995.1080p.BluRay.mkv", "Heat | 1995", "Matched", "Change", true),
            ("heat.mkv", "Heat | 3 possible matches", "Check this", "Pick match", false),
            ("movie_final_v2.mp4", "No film found for this file name", "No match", "Search…", false),
        ]);
        page.FindAll(".import-sum > span").Select(Text).ShouldBe(
            ["4 video files found", "2 matched", "2 need review", "0 already in collection as Digital"]);
        Chips(page).ShouldBe([("All", null), ("Needs review", "2"), ("Matched", "2")]);
        page.Find("button.add").TextContent.Trim().ShouldBe("Add 2 movies");
        Row(page, "arrival_2016.mp4").QuerySelector(".poster img")!.GetAttribute("src").ShouldBe("https://image.tmdb.org/t/p/w92/arrival.jpg");
        Row(page, "movie_final_v2.mp4").QuerySelector(".cb")!.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task The_chips_narrow_the_list()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv", "heat.mkv", "movie_final_v2.mp4");

        await Click(page, () => Chip(page, "Needs review"));
        Rows(page).Select(r => r.File).ShouldBe(["heat.mkv", "movie_final_v2.mp4"]);
        Chip(page, "Needs review").ClassList.ShouldContain("on");

        await Click(page, () => Chip(page, "Matched"));
        Rows(page).Select(r => r.File).ShouldBe(["Heat.1995.1080p.mkv"]);

        await Click(page, () => Chip(page, "All"));
        Rows(page).Length.ShouldBe(3);
    }

    [Fact]
    public async Task Add_puts_the_ticked_films_in_the_collection()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv", "arrival_2016.mp4", "heat.mkv");
        var changes = 0;
        Api.Changed += () => changes++;

        await Click(page, () => page.Find("button.add"));

        page.WaitForAssertion(() => Rows(page).Select(r => r.File).ShouldBe(["heat.mkv"]), Patience);
        var films = (await Api.ListMoviesAsync(new(), Ct)).Movies;
        films.Select(m => m.Title).Order().ShouldBe(["Arrival", "Heat"]);
        films.ShouldAllBe(m => m.Formats.SequenceEqual(new[] { MovieFormatKind.Digital }));
        (await FilmAsync(films.Single(m => m.Title == "Heat").Id)).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(InFolder("Heat.1995.1080p.mkv"));
        page.WaitForAssertion(() => page.Find("button.add").HasAttribute("disabled").ShouldBeTrue(), Patience);
        page.FindAll(".import-sum > span").Select(Text).ShouldBe(
            ["3 video files found", "0 matched", "1 needs review", "2 already in collection as Digital"]);
        changes.ShouldBeGreaterThan(0, "the library and the sidebar's counts hear of it");
    }

    [Fact]
    public async Task Unticking_a_file_leaves_it_out()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv", "arrival_2016.mp4");

        await Click(page, () => Row(page, "Heat.1995.1080p.mkv").QuerySelector(".cb")!);

        page.WaitForAssertion(() => page.Find("button.add").TextContent.Trim().ShouldBe("Add 1 movie"), Patience);
        Row(page, "Heat.1995.1080p.mkv").QuerySelector(".cb")!.GetAttribute("aria-checked").ShouldBe("false");

        await Click(page, () => page.Find("button.add"));

        page.WaitForAssertion(() => Rows(page).Select(r => r.File).ShouldBe(["Heat.1995.1080p.mkv"]), Patience);
        (await OnlyFilmAsync()).Title.ShouldBe("Arrival");
    }

    [Fact]
    public async Task Pick_match_lists_the_films_it_could_be_and_picking_one_ticks_the_file()
    {
        var page = await OpenImportAsync("heat.mkv");

        await Click(page, () => Row(page, "heat.mkv").QuerySelector("button.pick")!);

        page.Find(".imp-pick input").GetAttribute("value").ShouldBe("heat");
        page.FindAll(".imp-pick .match").Select(m => (m.QuerySelector(".mt")!.TextContent, m.QuerySelector(".mm")!.TextContent))
            .ShouldBe([("Heat", "1995"), ("Heat", "1986"), ("Heat and Dust", "1983")]);

        await Click(page, () => page.FindAll(".imp-pick .match")[1]);

        page.WaitForAssertion(() => Rows(page).ShouldBe([("heat.mkv", "Heat | 1986", "Matched", "Change", true)]), Patience);
        page.FindAll(".imp-pick").ShouldBeEmpty();
        page.Find("button.add").TextContent.Trim().ShouldBe("Add 1 movie");
    }

    [Fact]
    public async Task A_file_with_no_match_can_be_searched_for_by_hand()
    {
        var page = await OpenImportAsync("movie_final_v2.mp4");
        await Click(page, () => Row(page, "movie_final_v2.mp4").QuerySelector("button.pick")!);
        page.FindAll(".imp-pick .match").ShouldBeEmpty();

        await page.InvokeAsync(() => page.Find(".imp-pick input").Input("nothing like it"));
        await page.InvokeAsync(() => page.Find(".imp-pick form").Submit());
        page.WaitForAssertion(() => page.Find(".imp-pick .notice").TextContent.ShouldBe("No films found on TMDB for “nothing like it”."), Patience);

        await page.InvokeAsync(() => page.Find(".imp-pick input").Input("arrival 2016"));
        await page.InvokeAsync(() => page.Find(".imp-pick form").Submit());
        page.WaitForAssertion(() => page.FindAll(".imp-pick .match").ShouldHaveSingleItem(), Patience);
        await Click(page, () => page.Find(".imp-pick .match"));

        page.WaitForAssertion(() => Rows(page).ShouldBe([("movie_final_v2.mp4", "Arrival | 2016", "Matched", "Change", true)]), Patience);
    }

    [Fact]
    public async Task A_search_by_hand_says_when_tmdb_cannot_be_reached()
    {
        var page = await OpenImportAsync("heat.mkv");
        await Click(page, () => Row(page, "heat.mkv").QuerySelector("button.pick")!);
        Online.Tmdb.FailWith = MetadataProblem.Unavailable;

        await page.InvokeAsync(() => page.Find(".imp-pick form").Submit());

        page.WaitForAssertion(
            () => Text(page.Find(".imp-pick .notice")).ShouldBe("Couldn't reach TMDB. Check the internet connection and try again."), Patience);
    }

    [Fact]
    public async Task Skip_takes_a_file_off_the_list_and_the_skipped_chip_brings_it_back()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv", @"Extras\making-of.mkv");
        Chips(page).Select(c => c.Name).ShouldNotContain("Skipped");

        await Click(page, () => Row(page, @"Extras\making-of.mkv").QuerySelector("button.skip")!);

        page.WaitForAssertion(() => Rows(page).Select(r => r.File).ShouldBe(["Heat.1995.1080p.mkv"]), Patience);
        page.WaitForAssertion(() => Chips(page).ShouldContain(("Skipped", "1")), Patience);

        await Click(page, () => Chip(page, "Skipped"));
        page.Find(".imp.skipped .file").TextContent.ShouldBe(@"Extras\making-of.mkv");
        await Click(page, () => page.Find("button.back-in"));

        page.WaitForAssertion(() => Text(page.Find(".import-none")).ShouldBe("No skipped files."), Patience);
        await Click(page, () => Chip(page, "All"));
        page.WaitForAssertion(() => Rows(page).Select(r => r.File).ShouldBe([@"Extras\making-of.mkv", "Heat.1995.1080p.mkv"]), Patience);
    }

    [Fact]
    public async Task A_second_file_for_a_film_asks_whether_to_use_it_instead()
    {
        var first = Put("Heat.1995.1080p.mkv")[0];
        var heat = await SeedAsync(Film("Heat", 1995) with { TmdbId = 949, Formats = [new(MovieFormatKind.Digital, FilePath: first)] });

        var page = await OpenImportAsync("Heat.1995.2160p.mkv");

        Rows(page).ShouldBe([("Heat.1995.2160p.mkv", "Heat | 1995 · you already have a file for this film", "Check this", "Use this file instead", false)]);
        Row(page, "Heat.1995.2160p.mkv").QuerySelector(".cb")!.HasAttribute("disabled").ShouldBeTrue();
        Row(page, "Heat.1995.2160p.mkv").QuerySelectorAll("button.skip").ShouldHaveSingleItem();
        page.Find("button.add").HasAttribute("disabled").ShouldBeTrue();

        await Click(page, () => page.Find("button.use"));

        page.WaitForAssertion(() => Rows(page).ShouldBeEmpty(), Patience);
        (await FilmAsync(heat.Id)).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(InFolder("Heat.1995.2160p.mkv"));
        page.WaitForAssertion(() => Chips(page).ShouldContain(("Skipped", "1")), Patience);
    }

    [Fact]
    public async Task Of_two_files_for_one_film_the_second_says_so()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv", "Heat.1995.2160p.mkv");

        Rows(page).ShouldBe(
        [
            ("Heat.1995.1080p.mkv", "Heat | 1995", "Matched", "Change", true),
            ("Heat.1995.2160p.mkv", "Heat | 1995 · same film as Heat.1995.1080p.mkv", "Check this", "Change", false),
        ]);
        Chips(page).ShouldBe([("All", null), ("Needs review", "1"), ("Matched", "1")]);
    }

    [Fact]
    public async Task A_missing_file_is_listed_and_its_copy_can_be_removed()
    {
        var path = Put("Heat.1995.1080p.mkv")[0];
        var heat = await SeedAsync(Film("Heat", 1995, MovieFormatKind.BluRay) with
        {
            TmdbId = 949,
            Formats = [new(MovieFormatKind.BluRay), new(MovieFormatKind.Digital, FilePath: path)],
        });
        File.Delete(path);

        var page = await OpenImportAsync();

        Chips(page).ShouldContain(("Missing", "1"));
        await Click(page, () => Chip(page, "Missing"));
        var row = page.Find(".imp.gone");
        row.QuerySelector("a.ht")!.GetAttribute("href").ShouldBe($"movies/{heat.Id}");
        row.QuerySelector("a.ht")!.TextContent.ShouldBe("Heat1995");
        row.QuerySelector(".file")!.TextContent.ShouldBe(path);
        row.QuerySelector(".conf")!.TextContent.ShouldBe("File not found");

        await Click(page, () => page.Find("button.remove-copy"));

        page.WaitForAssertion(() => Text(page.Find(".import-none")).ShouldBe("No missing files."), Patience);
        (await FilmAsync(heat.Id)).Formats.ShouldHaveSingleItem().Format.ShouldBe(MovieFormatKind.BluRay);
    }

    [Fact]
    public async Task Removing_a_films_only_copy_asks_first_because_it_deletes_the_film()
    {
        var path = Put("Heat.1995.1080p.mkv")[0];
        await SeedAsync(Film("Heat", 1995) with { TmdbId = 949, Formats = [new(MovieFormatKind.Digital, FilePath: path)] });
        File.Delete(path);
        var page = await OpenImportAsync();
        await Click(page, () => Chip(page, "Missing"));

        await Click(page, () => page.Find("button.remove-copy"));

        Text(page.Find(".confirm")).ShouldStartWith("Delete Heat? Digital is the only format you own it on, so removing the copy removes the film.");
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();

        await Click(page, () => page.FindAll(".confirm button").Single(b => b.TextContent == "Cancel"));
        page.FindAll(".confirm").ShouldBeEmpty();

        await Click(page, () => page.Find("button.remove-copy"));
        await Click(page, () => page.Find(".confirm .danger"));

        page.WaitForAssertion(() => Text(page.Find(".import-none")).ShouldBe("No missing files."), Patience);
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Choose_file_says_where_a_missing_file_went()
    {
        var path = Put("Heat.1995.1080p.mkv")[0];
        var heat = await SeedAsync(Film("Heat", 1995) with { TmdbId = 949, Formats = [new(MovieFormatKind.Digital, FilePath: path)] });
        File.Delete(path);
        var page = await OpenImportAsync();
        await Click(page, () => Chip(page, "Missing"));

        FilePicker.Answer = Put("Heat.srt")[0];
        await Click(page, () => page.Find("button.locate"));
        page.WaitForAssertion(() => page.Find(".imp-note").TextContent.ShouldStartWith("That isn't a video file Shelf can link."), Patience);

        FilePicker.Answer = Put("Heat (1995) remux.mkv")[0];
        await Click(page, () => page.Find("button.locate"));

        page.WaitForAssertion(() => Text(page.Find(".import-none")).ShouldBe("No missing files."), Patience);
        (await FilmAsync(heat.Id)).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(InFolder("Heat (1995) remux.mkv"));
    }

    [Fact]
    public async Task Without_a_tmdb_key_the_screen_says_how_to_add_one()
    {
        Online.Tmdb.FailWith = MetadataProblem.NotConfigured;

        var page = await OpenImportAsync("Heat.1995.1080p.mkv");

        page.WaitForAssertion(() => page.Find(".notice").TextContent.ShouldContain("TMDB isn't set up on this PC yet."), Patience);
        Rows(page).ShouldBe([("Heat.1995.1080p.mkv", "Not looked up yet", "", "Skip", false)]);
    }

    [Fact]
    public async Task When_tmdb_is_back_try_again_looks_the_files_up()
    {
        Online.Tmdb.FailWith = MetadataProblem.Unavailable;
        var page = await OpenImportAsync("Heat.1995.1080p.mkv");
        page.WaitForAssertion(() => page.FindAll("button.retry").ShouldHaveSingleItem(), Patience);
        Online.Tmdb.FailWith = null;

        await Click(page, () => page.Find("button.retry"));

        page.WaitForAssertion(() => Rows(page).ShouldBe([("Heat.1995.1080p.mkv", "Heat | 1995", "Matched", "Change", true)]), Patience);
        page.FindAll(".notice").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_that_arrives_while_the_screen_is_open_is_looked_up()
    {
        var page = await OpenImportAsync("Heat.1995.1080p.mkv");

        Put("arrival_2016.mp4");
        Online.Watcher.Stir();

        page.WaitForAssertion(
            () => Rows(page).ShouldBe(
            [
                ("arrival_2016.mp4", "Arrival | 2016", "Matched", "Change", true),
                ("Heat.1995.1080p.mkv", "Heat | 1995", "Matched", "Change", true),
            ]),
            Patience);
    }

    [Fact]
    public async Task An_empty_folder_and_a_folder_with_nothing_left_say_so()
    {
        var page = await OpenImportAsync();
        Text(page.Find(".import-none")).ShouldBe("No video files in this folder yet.");

        var path = Put("Heat.1995.1080p.mkv")[0];
        await SeedAsync(Film("Heat", 1995) with { TmdbId = 949, Formats = [new(MovieFormatKind.Digital, FilePath: path)] });
        await Digital.RescanAsync(Ct);

        page.WaitForAssertion(
            () => Text(page.Find(".import-none")).ShouldBe("Nothing to review: every video file is in your collection or skipped."), Patience);
    }

    // ---- Play file ----

    [Fact]
    public async Task Play_file_opens_the_films_file_in_the_pcs_player()
    {
        var path = Put("Heat.1995.1080p.mkv")[0];
        var heat = await SeedAsync(Film("Heat", 1995) with { Formats = [new(MovieFormatKind.Dvd), new(MovieFormatKind.Digital, FilePath: path)] });
        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));
        page.WaitForElement("h1");

        page.FindAll("button.play").ShouldHaveSingleItem().TextContent.Trim().ShouldBe("Play file");
        page.Find(".owned-code + button.play").ShouldNotBeNull("the button sits under the file's path");
        page.FindAll(".file-problem").ShouldBeEmpty();

        await Click(page, () => page.Find("button.play"));

        page.WaitForAssertion(() => Online.Player.Opened.ShouldBe([path]), Patience);
        page.FindAll(".file-problem").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_that_is_gone_says_so_instead_of_offering_to_play()
    {
        var heat = await SeedAsync(Film("Heat", 1995) with { Formats = [new(MovieFormatKind.Digital, FilePath: InFolder("Heat.1995.1080p.mkv"))] });

        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));
        page.WaitForElement("h1");

        page.FindAll("button.play").ShouldBeEmpty();
        page.Find(".file-problem").TextContent.ShouldBe("File not found");
    }

    [Fact]
    public async Task A_file_the_pc_cannot_open_says_so()
    {
        var path = Put("Heat.1995.1080p.mkv")[0];
        var heat = await SeedAsync(Film("Heat", 1995) with { Formats = [new(MovieFormatKind.Digital, FilePath: path)] });
        Online.Player.Works = false;
        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));
        page.WaitForElement("h1");

        await Click(page, () => page.Find("button.play"));

        page.WaitForAssertion(() => page.Find(".file-problem").TextContent.ShouldStartWith("Couldn't open the file."), Patience);
    }

    [Fact]
    public async Task A_digital_copy_without_a_file_has_nothing_to_play()
    {
        var heat = await SeedAsync(Film("Heat", 1995, MovieFormatKind.Digital));

        var page = Render<MovieDetail>(p => p.Add(x => x.Id, heat.Id));
        page.WaitForElement("h1");

        page.FindAll("button.play").ShouldBeEmpty();
        page.FindAll(".file-problem").ShouldBeEmpty();
    }
}
