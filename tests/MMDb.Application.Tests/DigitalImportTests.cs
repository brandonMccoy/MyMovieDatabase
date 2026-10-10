using MMDb.Application.Digital;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>The movie folder: scanning it, matching its files to TMDB, adding them, and keeping track of skipped and missing files.</summary>
public sealed class DigitalImportTests : IAsyncLifetime
{
    private static readonly MovieDetails Arrival = TestMovies.Titled("Arrival", 2016) with { TmdbId = 329865, Directors = ["Denis Villeneuve"], PosterPath = "/arrival.jpg" };
    private static readonly MovieDetails BladeRunner2049 = TestMovies.Titled("Blade Runner 2049", 2017) with { TmdbId = 335984 };
    private static readonly MovieDetails Amelie = TestMovies.Titled("Amélie", 2001) with { TmdbId = 194 };

    private readonly TempFolder _movies = new();
    private TestShelf _shelf = null!;

    private string Folder => _movies.Path;

    private ImportStatus Status => _shelf.Importing;

    private FakeTmdb Tmdb => _shelf.Online.Tmdb;

    public async ValueTask InitializeAsync()
    {
        _shelf = await TestShelf.CreateAsync();
        Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb).Add(TestMovies.HeatAndDustOnTmdb).Add(Arrival).Add(BladeRunner2049).Add(Amelie);
    }

    public async ValueTask DisposeAsync()
    {
        await _shelf.DisposeAsync();
        _movies.Dispose();
    }

    /// <summary>Puts empty files in the movie folder and returns their full paths.</summary>
    private string[] Put(params string[] names) => [.. names.Select(name =>
    {
        var path = _movies[name];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    })];

    private async Task UseFolderAsync() => (await _shelf.Import((import, ct) => import.SetFolderAsync(Folder, ct))).ShouldBeTrue();

    private Task RescanAsync() => _shelf.Import((import, ct) => import.RescanAsync(ct));

    private Task MatchAsync() => _shelf.Import((import, ct) => import.MatchAsync(ct));

    private Task<int> AddTickedAsync() => _shelf.Import((import, ct) => import.AddTickedAsync(ct));

    /// <summary>Scans the folder with these files in it and looks them up.</summary>
    private async Task<string[]> MatchedAsync(params string[] names)
    {
        var paths = Put(names);
        await UseFolderAsync();
        await MatchAsync();
        return paths;
    }

    private ImportFile Row(string name) => Status.Files.Single(f => f.Name == name);

    private async Task<Movie> FilmAsync(Guid id) => (await _shelf.GetAsync(id)).ShouldNotBeNull();

    private async Task<Movie> OnlyFilmAsync() => await FilmAsync((await _shelf.ListAsync()).Movies.ShouldHaveSingleItem().Id);

    [Fact]
    public async Task Choosing_the_folder_lists_its_video_files_without_asking_tmdb()
    {
        Put("Heat.1995.1080p.BluRay.mkv", "arrival_2016.mp4", @"Sci-Fi\Blade.Runner.2049.2017.2160p.UHD.mkv", "Heat.1995.srt", "notes.txt");

        await UseFolderAsync();

        Status.State.ShouldBe(ImportFolderState.Ready);
        Status.Folder.ShouldBe(Folder);
        Status.VideoFileCount.ShouldBe(3);
        Status.LinkedCount.ShouldBe(0);
        Status.Files.Select(f => (f.Name, f.Parsed.Title, f.Parsed.Year, f.State)).ShouldBe(
        [
            ("arrival_2016.mp4", "arrival", 2016, FileMatchState.Waiting),
            ("Heat.1995.1080p.BluRay.mkv", "Heat", 1995, FileMatchState.Waiting),
            (@"Sci-Fi\Blade.Runner.2049.2017.2160p.UHD.mkv", "Blade Runner 2049", 2017, FileMatchState.Waiting),
        ]);
        Status.Files[0].Path.ShouldBe(_movies["arrival_2016.mp4"]);
        Tmdb.Searches.ShouldBeEmpty();
        (await _shelf.Settings.GetAsync("digital.folder", TestContext.Current.CancellationToken)).ShouldBe(Folder);
    }

    [Fact]
    public async Task A_folder_that_is_not_there_is_turned_down()
    {
        (await _shelf.Import((import, ct) => import.SetFolderAsync(_movies["gone"], ct))).ShouldBeFalse();
        (await _shelf.Import((import, ct) => import.SetFolderAsync("Movies", ct))).ShouldBeFalse();

        Status.State.ShouldBe(ImportFolderState.NotSetUp);
        Status.Folder.ShouldBeNull();
    }

    [Fact]
    public async Task A_name_with_the_title_and_year_of_one_film_is_matched_and_ticked()
    {
        await MatchedAsync("Heat.1995.1080p.BluRay.mkv", "arrival_2016.mp4");

        Status.Files.Select(f => (f.Name, f.State, f.Chosen?.Title, f.Chosen?.Year, f.Ticked)).ShouldBe(
        [
            ("arrival_2016.mp4", FileMatchState.Matched, "Arrival", 2016, true),
            ("Heat.1995.1080p.BluRay.mkv", FileMatchState.Matched, "Heat", 1995, true),
        ]);
        Row("arrival_2016.mp4").Chosen.ShouldBe(new MovieHit(329865, "Arrival", 2016, "/arrival.jpg"));
        Status.Progress.ShouldBeNull();
        Tmdb.Fetches.ShouldBe(0, "a film's details are only fetched when it is added");
    }

    [Fact]
    public async Task A_name_without_a_year_waits_for_review_with_the_films_it_could_be()
    {
        await MatchedAsync("heat.mkv");

        var file = Row("heat.mkv");
        file.State.ShouldBe(FileMatchState.Check);
        file.Ticked.ShouldBeFalse();
        file.Chosen?.TmdbId.ShouldBe(949);
        file.Hits.Select(h => (h.Title, h.Year)).ShouldBe([("Heat", 1995), ("Heat", 1986), ("Heat and Dust", 1983)]);
    }

    [Fact]
    public async Task A_title_that_is_only_close_waits_for_review()
    {
        await MatchedAsync("Heat and.1983.mkv");

        var file = Row("Heat and.1983.mkv");
        file.State.ShouldBe(FileMatchState.Check);
        file.Chosen?.Title.ShouldBe("Heat and Dust");
        file.Ticked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_name_no_film_is_found_for_has_no_match()
    {
        await MatchedAsync("movie_final_v2.mp4");

        var file = Row("movie_final_v2.mp4");
        file.State.ShouldBe(FileMatchState.NoMatch);
        file.Chosen.ShouldBeNull();
        file.Hits.ShouldBeEmpty();
        file.Ticked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_number_taken_for_the_year_is_searched_again_as_part_of_the_title()
    {
        await MatchedAsync("Blade.Runner.2049.mkv");

        Tmdb.Searches.ShouldBe([("Blade Runner", 2049), ("Blade Runner 2049", null)]);
        var file = Row("Blade.Runner.2049.mkv");
        file.State.ShouldBe(FileMatchState.Check, "the name gave no year after all");
        file.Chosen?.TmdbId.ShouldBe(335984);
    }

    [Fact]
    public async Task A_film_tmdb_dates_a_year_off_is_still_matched()
    {
        // Released at a festival in 2016, in cinemas in 2017: the file says 2017, TMDB says 2016.
        Tmdb.Add(TestMovies.Titled("Colossal", 2016) with { TmdbId = 339967 }, alsoReleasedIn: 2017);

        await MatchedAsync("Colossal.2017.1080p.mkv");

        Row("Colossal.2017.1080p.mkv").State.ShouldBe(FileMatchState.Matched);
        Row("Colossal.2017.1080p.mkv").Chosen?.TmdbId.ShouldBe(339967);
    }

    [Fact]
    public async Task Of_several_films_with_the_title_and_year_tmdbs_first_is_the_match()
    {
        Tmdb.Add(TestMovies.Titled("Arrival", 2016) with { TmdbId = 412345, Directors = ["A Student"] });

        await MatchedAsync("arrival_2016.mp4");

        var file = Row("arrival_2016.mp4");
        file.Hits.Select(h => h.TmdbId).ShouldBe([329865, 412345]);
        file.State.ShouldBe(FileMatchState.Matched);
        file.Chosen?.TmdbId.ShouldBe(329865);
        file.Ticked.ShouldBeTrue();
    }

    [Fact]
    public async Task Several_films_with_the_title_and_year_wait_for_review_when_tmdb_puts_another_film_first()
    {
        await using var other = await TestShelf.CreateAsync();
        other.Online.Tmdb
            .Add(TestMovies.Titled("The Arrival of Spring", 2016) with { TmdbId = 1 })
            .Add(TestMovies.Titled("Arrival", 2016) with { TmdbId = 2 })
            .Add(TestMovies.Titled("Arrival", 2016) with { TmdbId = 3 });
        Put("arrival_2016.mp4");
        (await other.Import((import, ct) => import.SetFolderAsync(Folder, ct))).ShouldBeTrue();

        await other.Import((import, ct) => import.MatchAsync(ct));

        var file = other.Importing.Files.ShouldHaveSingleItem();
        file.State.ShouldBe(FileMatchState.Check);
        file.Ticked.ShouldBeFalse();
    }

    [Fact]
    public async Task Files_are_only_looked_up_once()
    {
        await MatchedAsync("Heat.1995.mkv");
        Put("arrival_2016.mp4");
        await RescanAsync();

        await MatchAsync();

        Tmdb.Searches.ShouldBe([("Heat", 1995), ("arrival", 2016)]);
        Row("Heat.1995.mkv").State.ShouldBe(FileMatchState.Matched);
        Row("arrival_2016.mp4").State.ShouldBe(FileMatchState.Matched);
    }

    [Fact]
    public async Task When_tmdb_cannot_be_asked_the_files_wait_and_the_status_says_why()
    {
        Tmdb.FailWith = MetadataProblem.NotConfigured;
        await MatchedAsync("Heat.1995.mkv");

        Status.Problem.ShouldBe(MetadataProblem.NotConfigured);
        Status.FailedService.ShouldBe("TMDB");
        Status.Progress.ShouldBeNull();
        Row("Heat.1995.mkv").State.ShouldBe(FileMatchState.Waiting);

        Tmdb.FailWith = null;
        await MatchAsync();

        Status.Problem.ShouldBeNull();
        Row("Heat.1995.mkv").State.ShouldBe(FileMatchState.Matched);
    }

    [Fact]
    public async Task Adding_makes_each_ticked_file_a_film_with_a_digital_copy()
    {
        var paths = await MatchedAsync("Heat.1995.1080p.BluRay.mkv", "heat.mkv", "movie_final_v2.mp4");

        (await AddTickedAsync()).ShouldBe(1);

        var film = await OnlyFilmAsync();
        film.Title.ShouldBe("Heat");
        film.TmdbId.ShouldBe(949);
        film.Directors.ShouldBe(["Michael Mann"]);
        film.Crew.ShouldBe(TestMovies.HeatCrew);
        film.CoverImage.ShouldNotBeNull();
        var digital = film.Formats.ShouldHaveSingleItem();
        digital.Format.ShouldBe(MediaFormat.Digital);
        digital.FilePath.ShouldBe(paths[0]);

        Status.Files.Select(f => f.Name).ShouldBe(["heat.mkv", "movie_final_v2.mp4"]);
        Status.VideoFileCount.ShouldBe(3);
        Status.LinkedCount.ShouldBe(1);
        Status.Progress.ShouldBeNull();
        Tmdb.Fetches.ShouldBe(1);
    }

    [Fact]
    public async Task A_film_owned_on_disc_gets_the_digital_copy_added_to_it()
    {
        var owned = await _shelf.AddAsync(TestMovies.HeatOnTmdb with { Overview = "My own words." }, MediaFormat.BluRay, MediaFormat.Dvd);
        var paths = await MatchedAsync("Heat.1995.mkv");
        var fetches = Tmdb.Fetches;

        var file = Row("Heat.1995.mkv");
        file.Owned?.Id.ShouldBe(owned.Id);
        file.Owned?.Formats.ShouldBe([MediaFormat.BluRay, MediaFormat.Dvd]);
        file.OwnedFile.ShouldBeNull();
        file.Ticked.ShouldBeTrue();

        (await AddTickedAsync()).ShouldBe(1);

        var film = await OnlyFilmAsync();
        film.Id.ShouldBe(owned.Id);
        film.Overview.ShouldBe("My own words.");
        film.Formats.Select(f => (f.Format, f.FilePath)).ShouldBe([(MediaFormat.BluRay, null), (MediaFormat.Dvd, null), (MediaFormat.Digital, paths[0])]);
        Tmdb.Fetches.ShouldBe(fetches, "a film already owned keeps its details");
        Status.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_digital_copy_without_a_file_gets_the_file()
    {
        var owned = await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.Digital);
        var paths = await MatchedAsync("Heat.1995.mkv");

        Row("Heat.1995.mkv").Ticked.ShouldBeTrue();
        (await AddTickedAsync()).ShouldBe(1);

        (await FilmAsync(owned.Id)).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
    }

    [Fact]
    public async Task Files_that_are_a_films_copy_already_are_not_listed()
    {
        var paths = Put("Heat.1995.mkv", "arrival_2016.mp4");
        await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: paths[0].ToUpperInvariant())], ct));

        await UseFolderAsync();

        Status.Files.Select(f => f.Name).ShouldBe(["arrival_2016.mp4"]);
        Status.VideoFileCount.ShouldBe(2);
        Status.LinkedCount.ShouldBe(1);
        Status.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_file_for_a_film_is_not_ticked_and_cannot_be()
    {
        var paths = Put("Heat.1995.1080p.mkv", "Heat.1995.2160p.mkv");
        await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: paths[0])], ct));
        await UseFolderAsync();
        await MatchAsync();

        var file = Row("Heat.1995.2160p.mkv");
        file.OwnedFile.ShouldBe(paths[0]);
        file.Ticked.ShouldBeFalse();

        await _shelf.Import((import, _) =>
        {
            import.Tick(paths[1], true);
            return Task.CompletedTask;
        });
        Row("Heat.1995.2160p.mkv").Ticked.ShouldBeFalse();
        (await AddTickedAsync()).ShouldBe(0);
        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
    }

    [Fact]
    public async Task Using_the_second_file_instead_swaps_the_films_file_and_skips_the_old_one()
    {
        var paths = Put("Heat.1995.1080p.mkv", "Heat.1995.2160p.mkv");
        await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: paths[0])], ct));
        await UseFolderAsync();
        await MatchAsync();

        (await _shelf.Import((import, ct) => import.UseInsteadAsync(paths[1], ct))).ShouldBeTrue();

        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[1]);
        Status.Files.ShouldBeEmpty();
        Status.Skipped.ShouldBe([paths[0]]);
        Status.LinkedCount.ShouldBe(1);
        Status.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Of_two_new_files_for_one_film_the_second_is_not_ticked()
    {
        var paths = await MatchedAsync("Heat.1995.1080p.mkv", "Heat.1995.2160p.mkv");

        Row("Heat.1995.1080p.mkv").Ticked.ShouldBeTrue();
        var second = Row("Heat.1995.2160p.mkv");
        second.State.ShouldBe(FileMatchState.Matched);
        second.SameFilmAs.ShouldBe("Heat.1995.1080p.mkv");
        second.Ticked.ShouldBeFalse();

        (await AddTickedAsync()).ShouldBe(1);

        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
        Row("Heat.1995.2160p.mkv").OwnedFile.ShouldBe(paths[0]);
        Row("Heat.1995.2160p.mkv").SameFilmAs.ShouldBeNull();
    }

    [Fact]
    public async Task Ticking_both_files_of_one_film_still_adds_only_the_first()
    {
        var paths = await MatchedAsync("Heat.1995.1080p.mkv", "Heat.1995.2160p.mkv");
        await _shelf.Import((import, _) =>
        {
            import.Tick(paths[1], true);
            return Task.CompletedTask;
        });

        (await AddTickedAsync()).ShouldBe(1);

        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
        var left = Status.Files.ShouldHaveSingleItem();
        left.Name.ShouldBe("Heat.1995.2160p.mkv");
        left.Ticked.ShouldBeFalse();
        left.OwnedFile.ShouldBe(paths[0]);
    }

    [Fact]
    public async Task Picking_the_film_for_a_file_ticks_it()
    {
        var paths = await MatchedAsync("heat.mkv");
        var remake = Row("heat.mkv").Hits[1];

        await _shelf.Import((import, ct) => import.ChooseAsync(paths[0], remake, ct));

        var file = Row("heat.mkv");
        file.Chosen.ShouldBe(remake);
        file.State.ShouldBe(FileMatchState.Matched);
        file.Ticked.ShouldBeTrue();
        file.Hits.Count.ShouldBe(3);

        (await AddTickedAsync()).ShouldBe(1);
        (await OnlyFilmAsync()).Year.ShouldBe(1986);
    }

    [Fact]
    public async Task A_film_searched_for_by_hand_can_be_picked_for_a_file_with_no_match()
    {
        var paths = await MatchedAsync("movie_final_v2.mp4");

        var found = await _shelf.Import((import, ct) => import.SearchAsync("arrival 2016", ct));
        found.Hits.ShouldBe([new MovieHit(329865, "Arrival", 2016, "/arrival.jpg")]);
        await _shelf.Import((import, ct) => import.ChooseAsync(paths[0], found.Hits[0], ct));

        Row("movie_final_v2.mp4").Hits.ShouldBe(found.Hits);
        (await AddTickedAsync()).ShouldBe(1);
        var film = await OnlyFilmAsync();
        film.Title.ShouldBe("Arrival");
        film.Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
    }

    [Fact]
    public async Task A_search_by_hand_says_when_tmdb_cannot_be_asked()
    {
        Tmdb.FailWith = MetadataProblem.Unavailable;

        var found = await _shelf.Import((import, ct) => import.SearchAsync("arrival", ct));

        found.Hits.ShouldBeEmpty();
        found.Problem.ShouldBe(MetadataProblem.Unavailable);
        found.FailedService.ShouldBe("TMDB");
        (await _shelf.Import((import, ct) => import.SearchAsync("  ", ct))).ShouldBe(new HitSearch([]), "nothing to search for");
    }

    [Fact]
    public async Task Unticking_a_file_leaves_it_out_of_the_add()
    {
        var paths = await MatchedAsync("Heat.1995.mkv", "arrival_2016.mp4");
        await _shelf.Import((import, _) =>
        {
            import.Tick(paths[0], false);
            return Task.CompletedTask;
        });

        (await AddTickedAsync()).ShouldBe(1);

        (await OnlyFilmAsync()).Title.ShouldBe("Arrival");
        Status.Files.ShouldHaveSingleItem().Name.ShouldBe("Heat.1995.mkv");
    }

    [Fact]
    public async Task A_skipped_file_stays_away_through_scans_and_restarts_until_it_is_brought_back()
    {
        var paths = await MatchedAsync("Heat.1995.mkv", @"Extras\making-of.mkv");

        await _shelf.Import((import, ct) => import.SkipAsync(paths[1], ct));

        Status.Files.Select(f => f.Name).ShouldBe(["Heat.1995.mkv"]);
        Status.Skipped.ShouldBe([paths[1]]);

        await RescanAsync();
        Status.Files.Select(f => f.Name).ShouldBe(["Heat.1995.mkv"]);
        Status.Skipped.ShouldBe([paths[1]]);

        await _shelf.Import((import, ct) => import.StartAsync(ct));
        Status.Files.Select(f => f.Name).ShouldBe(["Heat.1995.mkv"]);
        Row("Heat.1995.mkv").State.ShouldBe(FileMatchState.Matched, "what was found out is kept");

        await _shelf.Import((import, ct) => import.BringBackAsync(paths[1], ct));

        Status.Skipped.ShouldBeEmpty();
        Status.Files.Select(f => (f.Name, f.State)).ShouldBe([(@"Extras\making-of.mkv", FileMatchState.Waiting), ("Heat.1995.mkv", FileMatchState.Matched)]);
        await RescanAsync();
        Status.Files.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_deleted_file_is_flagged_and_the_film_keeps_its_copy()
    {
        var paths = await MatchedAsync("Heat.1995.mkv", "arrival_2016.mp4");
        await AddTickedAsync();
        var films = (await _shelf.ListAsync()).Movies;

        File.Delete(paths[0]);
        await RescanAsync();

        var missing = Status.Missing.ShouldHaveSingleItem();
        missing.MovieId.ShouldBe(films.Single(m => m.Title == "Heat").Id);
        missing.Title.ShouldBe("Heat");
        missing.Year.ShouldBe(1995);
        missing.Path.ShouldBe(paths[0]);
        missing.OnlyCopy.ShouldBeTrue();
        missing.CoverImage.ShouldNotBeNull();
        Status.VideoFileCount.ShouldBe(1);
        Status.LinkedCount.ShouldBe(1);
        (await FilmAsync(missing.MovieId)).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);
    }

    [Fact]
    public async Task A_file_outside_the_movie_folder_is_never_called_missing()
    {
        await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: @"Z:\Elsewhere\Heat.mkv")], ct));

        await UseFolderAsync();

        Status.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_folder_itself_is_gone_nothing_is_called_missing()
    {
        var paths = await MatchedAsync("Heat.1995.mkv", "heat.mkv");
        await AddTickedAsync();

        Directory.Delete(Folder, recursive: true);
        await RescanAsync();

        Status.State.ShouldBe(ImportFolderState.FolderMissing);
        Status.Folder.ShouldBe(Folder);
        Status.Missing.ShouldBeEmpty();
        Status.Files.ShouldBeEmpty();
        _shelf.Online.Watcher.Watched.ShouldBeEmpty();
        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(paths[0]);

        Directory.CreateDirectory(Folder);
        Put("Heat.1995.mkv");
        await RescanAsync();

        Status.State.ShouldBe(ImportFolderState.Ready);
        Status.LinkedCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_file_moved_inside_the_folder_is_linked_again_by_its_name()
    {
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();

        Directory.CreateDirectory(_movies["Crime"]);
        File.Move(paths[0], _movies[@"Crime\Heat.1995.mkv"]);
        await RescanAsync();

        Status.Missing.ShouldBeEmpty();
        Status.Files.ShouldBeEmpty();
        Status.LinkedCount.ShouldBe(1);
        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(_movies[@"Crime\Heat.1995.mkv"]);
    }

    [Fact]
    public async Task A_missing_file_is_not_linked_to_one_of_several_files_with_its_name()
    {
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();

        File.Delete(paths[0]);
        Put(@"A\Heat.1995.mkv", @"B\Heat.1995.mkv");
        await RescanAsync();

        Status.Missing.ShouldHaveSingleItem().Path.ShouldBe(paths[0]);
        Status.Files.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Choosing_where_a_missing_file_went_links_the_film_to_it()
    {
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();
        var film = await OnlyFilmAsync();
        File.Delete(paths[0]);
        var renamed = Put("Heat (1995) remux.mkv")[0];
        await RescanAsync();

        (await _shelf.Import((import, ct) => import.LocateAsync(film.Id, _movies["nothing.mkv"], ct))).ShouldBeFalse("there is no such file");
        (await _shelf.Import((import, ct) => import.LocateAsync(film.Id, Put("Heat.srt")[0], ct))).ShouldBeFalse("not a video file");
        (await _shelf.Import((import, ct) => import.LocateAsync(film.Id, renamed, ct))).ShouldBeTrue();

        (await OnlyFilmAsync()).Formats.ShouldHaveSingleItem().FilePath.ShouldBe(renamed);
        Status.Missing.ShouldBeEmpty();
        Status.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_missing_copy_leaves_the_film_on_its_other_formats()
    {
        var owned = await _shelf.AddAsync(TestMovies.HeatOnTmdb, MediaFormat.BluRay);
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();
        File.Delete(paths[0]);
        await RescanAsync();
        Status.Missing.ShouldHaveSingleItem().OnlyCopy.ShouldBeFalse();

        (await _shelf.Import((import, ct) => import.RemoveCopyAsync(owned.Id, ct))).ShouldBeTrue();

        (await FilmAsync(owned.Id)).Formats.ShouldHaveSingleItem().Format.ShouldBe(MediaFormat.BluRay);
        Status.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_the_only_copy_removes_the_film()
    {
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();
        var film = await OnlyFilmAsync();
        File.Delete(paths[0]);
        await RescanAsync();

        (await _shelf.Import((import, ct) => import.RemoveCopyAsync(film.Id, ct))).ShouldBeTrue();

        (await _shelf.ListAsync()).Movies.ShouldBeEmpty();
        Status.Missing.ShouldBeEmpty();
        (await _shelf.Import((import, ct) => import.RemoveCopyAsync(film.Id, ct))).ShouldBeFalse("it is gone already");
    }

    [Fact]
    public async Task Starting_picks_up_the_folder_and_watches_it()
    {
        Put("Heat.1995.mkv");
        await UseFolderAsync();
        _shelf.Online.Watcher.Watched.ShouldBe([Folder]);

        await using var restarted = await TestShelf.CreateAsync();
        restarted.Importing.State.ShouldBe(ImportFolderState.NotSetUp);
        await restarted.Import((import, ct) => import.StartAsync(ct));
        restarted.Importing.State.ShouldBe(ImportFolderState.NotSetUp, "nothing was ever chosen on that PC");

        await _shelf.Import((import, ct) => import.StartAsync(ct));
        Status.State.ShouldBe(ImportFolderState.Ready);
        Status.Files.ShouldHaveSingleItem();
        Status.Watch.ShouldBeTrue();
    }

    [Fact]
    public async Task The_watcher_asks_for_a_scan_when_files_change()
    {
        await UseFolderAsync();
        var asked = 0;
        _shelf.ImportDesk.FolderChanged += () => asked++;

        Put("Heat.1995.mkv");
        _shelf.Online.Watcher.Stir();

        asked.ShouldBe(1);
        Status.Files.ShouldBeEmpty("the scan itself is the listener's to run");
        await RescanAsync();
        Status.Files.ShouldHaveSingleItem().Name.ShouldBe("Heat.1995.mkv");
    }

    [Fact]
    public async Task Watching_can_be_turned_off_and_on_and_is_remembered()
    {
        await UseFolderAsync();

        await _shelf.Import((import, ct) => import.SetWatchAsync(false, ct));

        Status.Watch.ShouldBeFalse();
        _shelf.Online.Watcher.Watched.ShouldBeEmpty();
        await _shelf.Import((import, ct) => import.StartAsync(ct));
        Status.Watch.ShouldBeFalse();
        _shelf.Online.Watcher.Watched.ShouldBeEmpty();

        Put("Heat.1995.mkv");
        await _shelf.Import((import, ct) => import.SetWatchAsync(true, ct));

        Status.Watch.ShouldBeTrue();
        _shelf.Online.Watcher.Watched.ShouldBe([Folder]);
        Status.Files.ShouldHaveSingleItem("turning it on catches up");
    }

    [Fact]
    public async Task Changing_the_folder_forgets_the_other_folders_files()
    {
        await MatchedAsync("Heat.1995.mkv");
        using var other = new TempFolder();
        File.WriteAllBytes(other["arrival_2016.mp4"], []);

        (await _shelf.Import((import, ct) => import.SetFolderAsync(other.Path, ct))).ShouldBeTrue();

        Status.Folder.ShouldBe(other.Path);
        Status.Files.ShouldHaveSingleItem().Name.ShouldBe("arrival_2016.mp4");
        _shelf.Online.Watcher.Watched.ShouldBe([other.Path]);
    }

    [Fact]
    public async Task A_few_films_are_written_to_the_vault_one_by_one()
    {
        using var vault = new TempFolder();
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(vault.Path, ct));
        await MatchedAsync("Heat.1995.mkv", "arrival_2016.mp4");

        await AddTickedAsync();

        _shelf.Vault.Calls.ShouldBe([$"sync {vault.Path} (0 films)", "write Arrival", "write Heat"]);
    }

    [Fact]
    public async Task A_big_import_writes_the_vault_once_at_the_end()
    {
        using var vault = new TempFolder();
        await _shelf.VaultAsync((sync, ct) => sync.SetFolderAsync(vault.Path, ct));
        for (var i = 1; i <= 12; i++)
        {
            Tmdb.Add(TestMovies.Titled($"Film Number {i:00}", 2000 + i) with { TmdbId = 5000 + i });
        }

        await MatchedAsync([.. Enumerable.Range(1, 12).Select(i => $"Film.Number.{i:00}.{2000 + i}.1080p.mkv")]);

        (await AddTickedAsync()).ShouldBe(12);

        _shelf.Vault.Calls.ShouldBe([$"sync {vault.Path} (0 films)", $"sync {vault.Path} (12 films)"]);
        _shelf.Vault.SyncedFilms.Count.ShouldBe(12);
        Status.Files.ShouldBeEmpty();
        Status.LinkedCount.ShouldBe(12);
    }

    [Fact]
    public async Task Playing_opens_the_films_file()
    {
        var paths = await MatchedAsync("Heat.1995.mkv");
        await AddTickedAsync();
        var film = await OnlyFilmAsync();

        (await _shelf.Import((import, ct) => import.PlayAsync(film.Id, ct))).ShouldBeTrue();

        _shelf.Online.Player.Opened.ShouldBe([paths[0]]);
    }

    [Fact]
    public async Task Nothing_is_opened_for_a_missing_file_or_one_that_is_not_a_video()
    {
        var paths = Put("Heat.1995.mkv", "setup.exe");
        var missing = await _shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: paths[0])], ct));
        var program = await _shelf.Movies((catalog, ct) => catalog.AddAsync(Arrival, [new(MediaFormat.Digital, FilePath: paths[1])], ct));
        var disc = await _shelf.AddAsync(Amelie, MediaFormat.Dvd);
        File.Delete(paths[0]);

        foreach (var id in new[] { ((SaveMovieResult.Saved)missing).Movie.Id, ((SaveMovieResult.Saved)program).Movie.Id, disc.Id, Guid.NewGuid() })
        {
            (await _shelf.Import((import, ct) => import.PlayAsync(id, ct))).ShouldBeFalse();
        }

        _shelf.Online.Player.Opened.ShouldBeEmpty();
    }
}
