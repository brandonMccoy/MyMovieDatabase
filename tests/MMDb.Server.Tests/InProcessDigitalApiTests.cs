using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Digital;
using MMDb.Contracts.Movies;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>The PC wiring for digital films: the folder's watcher, what the screens are handed, and telling them of saves.</summary>
public sealed class InProcessDigitalApiTests : IAsyncLifetime
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly TempFolder _movies = new();
    private readonly FakeOnline _online = new();
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private IDigitalApi Digital => _services.GetRequiredService<IDigitalApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero)));
        _services = _online.ReplaceIn(services).BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
        _online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
        _movies.Dispose();
    }

    private string Put(string name)
    {
        File.WriteAllBytes(_movies[name], []);
        return _movies[name];
    }

    private static async Task EventuallyAsync(Action assertion)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                assertion();
                return;
            }
            catch (ShouldAssertException) when (attempt < 200)
            {
                await Task.Delay(25, Ct);
            }
        }
    }

    [Fact]
    public async Task Starting_reads_the_remembered_folder_and_watches_it()
    {
        Put("Heat.1995.1080p.mkv");
        (await Digital.SetFolderAsync(_movies.Path, Ct)).ShouldBeTrue();

        await Digital.StartAsync(Ct);

        var folder = Digital.Folder;
        folder.State.ShouldBe(MovieFolderState.Ready);
        folder.Folder.ShouldBe(_movies.Path);
        folder.Watch.ShouldBeTrue();
        folder.VideoFileCount.ShouldBe(1);
        var file = folder.Files.ShouldHaveSingleItem();
        file.Path.ShouldBe(_movies["Heat.1995.1080p.mkv"]);
        file.Name.ShouldBe("Heat.1995.1080p.mkv");
        file.Reading.ShouldBe("Heat 1995");
        file.Match.ShouldBe(FileMatch.Waiting);
        _online.Watcher.Watched.ShouldBe([_movies.Path]);
        Digital.Folder.ShouldBeSameAs(folder, "the status is mapped once per change");
    }

    [Fact]
    public async Task When_the_watcher_says_files_changed_the_folder_is_scanned_again()
    {
        (await Digital.SetFolderAsync(_movies.Path, Ct)).ShouldBeTrue();
        var changes = 0;
        Digital.Changed += () => Interlocked.Increment(ref changes);

        Put("Heat.1995.1080p.mkv");
        _online.Watcher.Stir();

        await EventuallyAsync(() => Digital.Folder.Files.ShouldHaveSingleItem().Name.ShouldBe("Heat.1995.1080p.mkv"));
        changes.ShouldBeGreaterThan(0);
        _online.Tmdb.Searches.ShouldBeEmpty("a scan asks TMDB nothing");
    }

    [Fact]
    public async Task Looked_up_files_come_with_the_film_and_a_small_poster()
    {
        Put("Heat.1995.1080p.mkv");
        await Digital.SetFolderAsync(_movies.Path, Ct);

        await Digital.MatchAsync(Ct);

        var file = Digital.Folder.Files.ShouldHaveSingleItem();
        file.Match.ShouldBe(FileMatch.Matched);
        file.Ticked.ShouldBeTrue();
        file.Film.ShouldBe(new FilmHitDto(949, "Heat", 1995, new Uri("https://image.tmdb.org/t/p/w92/heat.jpg"), "/heat.jpg"));
        file.Hits.ShouldBe([file.Film!]);
        Digital.Folder.Progress.ShouldBeNull();
    }

    [Fact]
    public async Task Adding_tells_the_open_screens_and_the_film_is_in_the_collection()
    {
        var path = Put("Heat.1995.1080p.mkv");
        await Digital.SetFolderAsync(_movies.Path, Ct);
        await Digital.MatchAsync(Ct);
        var saves = 0;
        Api.Changed += () => saves++;

        (await Digital.AddTickedAsync(Ct)).ShouldBe(1);

        saves.ShouldBe(1);
        var film = (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();
        film.Formats.ShouldBe([MovieFormatKind.Digital]);
        (await Api.GetMovieAsync(film.Id, Ct))!.Formats.ShouldHaveSingleItem().FilePath.ShouldBe(path);
        Digital.Folder.Files.ShouldBeEmpty();
        Digital.Folder.LinkedCount.ShouldBe(1);

        (await Digital.AddTickedAsync(Ct)).ShouldBe(0);
        saves.ShouldBe(1, "nothing was added the second time");
    }

    [Fact]
    public async Task A_search_by_hand_answers_with_films_none_or_why_not()
    {
        var found = await Digital.SearchAsync("heat", Ct);
        found.Status.ShouldBe(LookupStatus.Found);
        found.Hits.Select(h => (h.TmdbId, h.Title, h.Year)).ShouldBe([(949, "Heat", 1995), (31939, "Heat", 1986)]);

        (await Digital.SearchAsync("no such film", Ct)).Status.ShouldBe(LookupStatus.NoMatches);

        _online.Tmdb.FailWith = MetadataProblem.KeyRejected;
        var failed = await Digital.SearchAsync("heat", Ct);
        failed.Status.ShouldBe(LookupStatus.KeyRejected);
        failed.FailedService.ShouldBe("TMDB");
        failed.Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lookup_that_fails_says_why_in_the_status()
    {
        Put("Heat.1995.1080p.mkv");
        await Digital.SetFolderAsync(_movies.Path, Ct);
        _online.Tmdb.FailWith = MetadataProblem.NotConfigured;

        await Digital.MatchAsync(Ct);

        Digital.Folder.Problem.ShouldBe(LookupStatus.NotConfigured);
        Digital.Folder.FailedService.ShouldBe("TMDB");
        Digital.Folder.Files.ShouldHaveSingleItem().Match.ShouldBe(FileMatch.Waiting);
    }

    [Fact]
    public async Task Skipped_and_missing_files_are_handed_over_with_names_and_covers()
    {
        var heat = Put("Heat.1995.1080p.mkv");
        var extra = Put("making-of.mkv");
        await Digital.SetFolderAsync(_movies.Path, Ct);
        await Digital.MatchAsync(Ct);
        await Digital.AddTickedAsync(Ct);
        var film = (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem();

        await Digital.SkipAsync(extra, Ct);
        File.Delete(heat);
        await Digital.RescanAsync(Ct);

        Digital.Folder.Skipped.ShouldBe([new SkippedFileDto(extra, "making-of.mkv")]);
        var missing = Digital.Folder.Missing.ShouldHaveSingleItem();
        missing.ShouldBe(new MissingFileDto(film.Id, "Heat", 1995, heat, OnlyCopy: true, film.CoverUrl));
        missing.CoverUrl.ShouldNotBeNull();
        Digital.FileExists(heat).ShouldBeFalse();
        Digital.FileExists(extra).ShouldBeTrue();
    }
}
