using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Vault;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Vault;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>The vault on disk: what the notes say, where they go, and that only Shelf's own files are touched.</summary>
public sealed class FileVaultWriterTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _vault = new();
    private readonly TempFolder _covers = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FileVaultWriter _writer;

    public FileVaultWriterTests() => _writer = new(_covers.Path, _clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folder => _vault.Path;

    public void Dispose()
    {
        _vault.Dispose();
        _covers.Dispose();
    }

    private static MovieDetails FullHeat => TestMovies.HeatOnTmdb with
    {
        Cast = [new("Al Pacino", "Lt. Vincent Hanna"), new("Robert De Niro", "Neil McCauley"), new("Val Kilmer", "Chris Shiherlis")],
        Genres = ["Crime", "Drama", "Action"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "Obsessive master thief Neil McCauley leads a top-notch crew on daring heists throughout Los Angeles.",
    };

    private Movie Film(MovieDetails details, params FormatDetails[] formats) =>
        Movie.Create(details, formats.Length == 0 ? TestMovies.On(MediaFormat.Dvd) : formats, _clock.GetUtcNow());

    private Movie WithCover(Movie movie, string name = "heat.jpg")
    {
        var cover = $"{movie.Id:N}-{name}";
        File.WriteAllText(_covers[cover], "image of " + name);
        movie.UseCover(cover);
        return movie;
    }

    private static VaultContent Films(params Movie[] movies) => new([.. movies.Select(m => m.Listing)], [], []);

    private Task SyncAsync(params Movie[] movies) => _writer.SyncAsync(Folder, Films(movies), movies, Ct);

    private Task WriteAsync(Movie movie, params Movie[] others) => _writer.WriteMovieAsync(Folder, movie, Films([movie, .. others]), Ct);

    private string Read(string relative) => File.ReadAllText(_vault[relative]);

    private string[] Notes() => FileNames("Movies");

    private string[] FileNames(string folder) =>
        [.. Directory.GetFiles(_vault[folder]).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    private static string Scrub(string text, params Movie[] movies) =>
        movies.Aggregate(text, (t, m) => t.Replace(m.Id.ToString("D"), "{mmdb_id}", StringComparison.Ordinal).Replace(m.Id.ToString("N"), "{id}", StringComparison.Ordinal));

    private static Task VerifyText(string text, string extension = "md", [CallerMemberName] string test = "")
    {
        Snapshot.Match(text, $"{nameof(FileVaultWriterTests)}.{test}", extension);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_film_from_tmdb_gets_a_full_note()
    {
        var heat = WithCover(Film(
            FullHeat,
            new FormatDetails(MediaFormat.Uhd4K, Upc: "786936894424"),
            new FormatDetails(MediaFormat.Dvd),
            new FormatDetails(MediaFormat.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")));

        await WriteAsync(heat);

        await VerifyText(Scrub(Read("Movies/Heat (1995).md"), heat));
    }

    [Fact]
    public async Task The_cast_names_the_actors_and_not_who_they_play()
    {
        var heat = Film(FullHeat);

        await WriteAsync(heat);

        var note = Read("Movies/Heat (1995).md");
        note.ShouldContain("**Cast** [[Al Pacino]], [[Robert De Niro]], [[Val Kilmer]]\n");
        note.ShouldNotContain("Vincent Hanna");
        note.ShouldNotContain("Chris Shiherlis");
    }

    [Fact]
    public async Task Headline_crew_are_linked_like_the_directors()
    {
        await WriteAsync(Film(FullHeat));

        Read("Movies/Heat (1995).md").ShouldContain(
            "**Directed by** [[Michael Mann]]\n"
            + "**Written by** [[Michael Mann]]\n"
            + "**Produced by** [[Art Linson]], [[Michael Mann]]\n"
            + "**Executive producers** [[Arnon Milchan]]\n"
            + "**Cinematography** [[Dante Spinotti]]\n"
            + "**Music** [[Elliot Goldenthal]]\n"
            + "**Edited by** [[Pasquale Buba]]\n"
            + "**Cast** [[Al Pacino]]");
    }

    [Fact]
    public async Task Everyone_in_the_cast_is_a_link_and_the_frontmatter_keeps_the_top_ten()
    {
        var cast = Enumerable.Range(1, 12).Select(i => new CastMember($"Actor {i}", $"Part {i}"));

        await WriteAsync(Film(FullHeat with { Cast = [.. cast] }));

        var note = Read("Movies/Heat (1995).md");
        note.ShouldContain("**Cast** [[Actor 1]], [[Actor 2]]");
        note.ShouldContain("[[Actor 11]], [[Actor 12]]\n");
        note.ShouldContain("cast:\n  - Actor 1\n");
        note.ShouldContain("  - Actor 10\ngenres:");
    }

    [Fact]
    public async Task The_rest_of_the_crew_is_linked_under_its_job_title()
    {
        await WriteAsync(Film(FullHeat with
        {
            Crew =
            [
                .. TestMovies.HeatCrew,
                new("Kathy Shorkey", "Set Designer", "Art"),
                new("Bud Davis", "Stunts", "Crew"),
                new("Bud Davis", "Stunt Coordinator", "Crew"),
            ],
        }));

        Read("Movies/Heat (1995).md").ShouldContain(
            "\n\n## Crew\n"
            + "**First Assistant Director** [[Michael Waxman]]\n"
            + "**Associate Producer** [[Gusmano Cesaretti]]\n"
            + "**Production Design** [[Neil Spisak]]\n"
            + "**Set Designer** [[Neil Spisak]], [[Kathy Shorkey]]\n"
            + "**Stunt Coordinator** [[Bud Davis]]\n"
            + "**Stunts** [[Bud Davis]]\n"
            + "\n%% Shelf rewrites");
    }

    [Fact]
    public async Task A_film_with_nothing_more_to_credit_has_no_such_section()
    {
        await WriteAsync(Film(FullHeat with { Crew = [new("Dante Spinotti", "Director of Photography", "Camera")] }));

        var note = Read("Movies/Heat (1995).md");
        note.ShouldContain("**Cinematography** [[Dante Spinotti]]\n");
        note.ShouldNotContain("## Crew");
    }

    [Fact]
    public async Task A_crew_credit_cannot_break_its_link_or_turn_into_formatting()
    {
        await WriteAsync(Film(FullHeat with { Crew = [new("Some [[Body|else]]", "Thanks *a lot*")] }));

        Read("Movies/Heat (1995).md").ShouldContain("**Thanks \\*a lot\\*** Some \\[\\[Body\\|else\\]\\]\n");
    }

    [Fact]
    public async Task A_film_entered_by_hand_gets_a_short_note()
    {
        var film = Film(TestMovies.Titled("Mission: Impossible"));

        await WriteAsync(film);

        await VerifyText(Scrub(Read("Movies/Mission - Impossible.md"), film));
    }

    [Fact]
    public async Task The_index_lists_every_film_with_the_totals()
    {
        var heat = Film(TestMovies.Heat, new FormatDetails(MediaFormat.Uhd4K), new FormatDetails(MediaFormat.Dvd));
        var alien = Film(TestMovies.Titled("Alien", 1979) with { Directors = ["Ridley Scott"] }, new FormatDetails(MediaFormat.BluRay));
        var arrival = Film(TestMovies.Titled("arrival", 2016), new FormatDetails(MediaFormat.Digital));

        await SyncAsync(heat, alien, arrival);

        await VerifyText(Read("_Index.md"));
    }

    [Fact]
    public async Task The_guide_for_claude_describes_the_vault()
    {
        await SyncAsync();

        await VerifyText(Read("CLAUDE.md"));
    }

    [Fact]
    public async Task The_movies_base_has_cover_table_and_recent_views()
    {
        await SyncAsync();

        await VerifyText(Read("Movies.base"), "yaml");
    }

    [Fact]
    public async Task The_note_can_be_found_by_film()
    {
        var heat = Film(TestMovies.Heat);

        await WriteAsync(heat);

        _writer.NoteOf(Folder, heat.Id).ShouldBe("Movies/Heat (1995).md");
        _writer.NoteOf(Folder, Guid.NewGuid()).ShouldBeNull();
        _writer.NoteOf(_covers.Path, heat.Id).ShouldBeNull();
    }

    [Fact]
    public async Task The_added_date_is_the_local_day()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC-10", TimeSpan.FromHours(-10), "UTC-10", "UTC-10"));

        await WriteAsync(Film(TestMovies.Heat));

        Read("Movies/Heat (1995).md").ShouldContain("\nadded: 2026-01-14\n");
    }

    [Fact]
    public async Task Rewriting_a_note_keeps_what_the_user_wrote_under_my_notes()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        var path = _vault["Movies/Heat (1995).md"];
        await File.AppendAllTextAsync(path, "\nBest bank robbery ever.\r\n- [[Michael Mann]] commentary\n", Ct);

        heat.Update(TestMovies.Heat with { Rating = "R" }, TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());
        await WriteAsync(heat);

        var note = Read("Movies/Heat (1995).md");
        note.ShouldContain("\nrating: R\n");
        note.ShouldEndWith("## My notes\n\nBest bank robbery ever.\r\n- [[Michael Mann]] commentary\n");
    }

    [Fact]
    public async Task A_new_title_moves_the_note_and_keeps_my_notes()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        await File.AppendAllTextAsync(_vault["Movies/Heat (1995).md"], "Mine.\n", Ct);

        heat.Update(TestMovies.Heat with { Title = "Heat: Director's Definitive Edition" }, TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());
        await WriteAsync(heat);

        Notes().ShouldBe(["Heat - Director's Definitive Edition (1995).md"]);
        Read("Movies/Heat - Director's Definitive Edition (1995).md").ShouldEndWith("## My notes\nMine.\n");
        _writer.NoteOf(Folder, heat.Id).ShouldBe("Movies/Heat - Director's Definitive Edition (1995).md");
    }

    [Fact]
    public async Task A_change_of_case_renames_the_file()
    {
        var heat = Film(TestMovies.Titled("heat", 1995));
        await WriteAsync(heat);

        heat.Update(TestMovies.Titled("Heat", 1995), TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());
        await WriteAsync(heat);

        Notes().ShouldBe(["Heat (1995).md"]);
    }

    [Fact]
    public async Task Two_films_with_the_same_name_get_a_note_each()
    {
        var first = Film(TestMovies.Heat);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = Film(TestMovies.Heat with { Directors = ["Someone Else"] });

        await WriteAsync(first);
        await WriteAsync(second, first);

        Notes().ShouldBe(["Heat (1995) (2).md", "Heat (1995).md"]);
        _writer.NoteOf(Folder, second.Id).ShouldBe("Movies/Heat (1995) (2).md");
        Read("_Index.md").ShouldContain("- [[Heat (1995) (2)]] · Someone Else · DVD\n");
    }

    [Fact]
    public async Task A_rebuild_gives_the_name_to_the_film_added_first()
    {
        var first = Film(TestMovies.Heat);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = Film(TestMovies.Heat with { Directors = ["Someone Else"] });

        await SyncAsync(second, first);

        _writer.NoteOf(Folder, first.Id).ShouldBe("Movies/Heat (1995).md");
        _writer.NoteOf(Folder, second.Id).ShouldBe("Movies/Heat (1995) (2).md");
    }

    [Fact]
    public async Task A_note_the_user_wrote_is_never_overwritten()
    {
        Directory.CreateDirectory(_vault["Movies"]);
        await File.WriteAllTextAsync(_vault["Movies/Heat (1995).md"], "My own Heat note.", Ct);
        var heat = Film(TestMovies.Heat);

        await WriteAsync(heat);

        Read("Movies/Heat (1995).md").ShouldBe("My own Heat note.");
        _writer.NoteOf(Folder, heat.Id).ShouldBe("Movies/Heat (1995) (2).md");
    }

    [Fact]
    public async Task Removing_a_film_deletes_its_note_and_cover()
    {
        var heat = WithCover(Film(TestMovies.HeatOnTmdb));
        await WriteAsync(heat);

        await _writer.RemoveAsync(Folder, heat.Id, VaultContent.Empty, Ct);

        Notes().ShouldBeEmpty();
        FileNames("Attachments/covers").ShouldBeEmpty();
        Directory.Exists(_vault[".trash"]).ShouldBeFalse();
        _writer.NoteOf(Folder, heat.Id).ShouldBeNull();
        Read("_Index.md").ShouldContain("**Movies:** 0 films\n");
    }

    [Fact]
    public async Task A_removed_film_with_my_notes_goes_to_the_trash()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        await File.AppendAllTextAsync(_vault["Movies/Heat (1995).md"], "Lent to Sam.\n", Ct);

        await _writer.RemoveAsync(Folder, heat.Id, VaultContent.Empty, Ct);

        Notes().ShouldBeEmpty();
        File.ReadAllText(_vault[".trash/Heat (1995).md"]).ShouldEndWith("Lent to Sam.\n");
    }

    [Fact]
    public async Task The_trash_never_overwrites_an_earlier_note()
    {
        Directory.CreateDirectory(_vault[".trash"]);
        await File.WriteAllTextAsync(_vault[".trash/Heat (1995).md"], "older", Ct);
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        await File.AppendAllTextAsync(_vault["Movies/Heat (1995).md"], "Lent to Sam.\n", Ct);

        await _writer.RemoveAsync(Folder, heat.Id, VaultContent.Empty, Ct);

        File.ReadAllText(_vault[".trash/Heat (1995).md"]).ShouldBe("older");
        File.ReadAllText(_vault[".trash/Heat (1995) (2).md"]).ShouldEndWith("Lent to Sam.\n");
    }

    [Fact]
    public async Task A_rebuild_removes_notes_of_films_no_longer_owned_and_leaves_the_users_files()
    {
        var heat = Film(TestMovies.Heat);
        var alien = Film(TestMovies.Titled("Alien", 1979));
        await SyncAsync(heat, alien);
        await File.WriteAllTextAsync(_vault["Movies/Watch next.md"], "Thief (1981)", Ct);
        await File.WriteAllTextAsync(_vault["Movies/Copy.md"], "---\nmmdb_id: 1\n---\nnot managed", Ct);
        await File.WriteAllTextAsync(_vault["Ideas.md"], "", Ct);

        await SyncAsync(alien);

        Notes().ShouldBe(["Alien (1979).md", "Copy.md", "Watch next.md"]);
        File.Exists(_vault["Ideas.md"]).ShouldBeTrue();
    }

    [Fact]
    public async Task A_rebuild_finds_notes_written_before_the_app_restarted()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        await File.AppendAllTextAsync(_vault["Movies/Heat (1995).md"], "Mine.\n", Ct);
        var restarted = new FileVaultWriter(_covers.Path, _clock);
        heat.Update(TestMovies.Titled("Heat", 1996), TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());

        await restarted.SyncAsync(Folder, Films(heat), [heat], Ct);

        Notes().ShouldBe(["Heat (1996).md"]);
        Read("Movies/Heat (1996).md").ShouldEndWith("Mine.\n");
    }

    [Fact]
    public async Task The_cover_is_copied_into_the_vault_and_embedded()
    {
        var heat = WithCover(Film(TestMovies.HeatOnTmdb));

        await WriteAsync(heat);

        var cover = $"Attachments/covers/{heat.Id:N}-heat.jpg";
        Read(cover).ShouldBe("image of heat.jpg");
        var note = Read("Movies/Heat (1995).md");
        note.ShouldContain($"\ncover: \"[[{cover}]]\"\n");
        note.ShouldContain($"\n![[{cover}|200]]\n");
    }

    [Fact]
    public async Task A_new_poster_replaces_the_old_cover()
    {
        var heat = WithCover(Film(TestMovies.HeatOnTmdb));
        await WriteAsync(heat);

        WithCover(heat, "heat-new.jpg");
        await WriteAsync(heat);

        FileNames("Attachments/covers").ShouldBe([$"{heat.Id:N}-heat-new.jpg"]);
    }

    [Fact]
    public async Task A_film_whose_cover_never_downloaded_has_no_cover_in_its_note()
    {
        var heat = Film(TestMovies.HeatOnTmdb);
        heat.UseCover($"{heat.Id:N}-heat.jpg");

        await WriteAsync(heat);

        Read("Movies/Heat (1995).md").ShouldNotContain("cover");
    }

    [Fact]
    public async Task A_rebuild_removes_covers_of_films_no_longer_owned_and_leaves_other_images()
    {
        var heat = WithCover(Film(TestMovies.HeatOnTmdb));
        var alien = WithCover(Film(TestMovies.Titled("Alien", 1979)), "alien.jpg");
        await SyncAsync(heat, alien);
        await File.WriteAllTextAsync(_vault["Attachments/covers/my-scan.jpg"], "", Ct);

        await SyncAsync(alien);

        FileNames("Attachments/covers").ShouldBe([$"{alien.Id:N}-alien.jpg", "my-scan.jpg"]);
    }

    [Fact]
    public async Task An_unchanged_note_is_not_written_again()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        var path = _vault["Movies/Heat (1995).md"];
        var longAgo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, longAgo);

        await SyncAsync(heat);

        File.GetLastWriteTimeUtc(path).ShouldBe(longAgo);
    }

    [Fact]
    public async Task No_temporary_files_are_left_behind()
    {
        await SyncAsync(WithCover(Film(TestMovies.HeatOnTmdb)));

        Directory.GetFiles(Folder, "*.shelf-tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_users_own_claude_md_and_base_are_left_alone()
    {
        await File.WriteAllTextAsync(_vault["CLAUDE.md"], "# My vault rules", Ct);
        await File.WriteAllTextAsync(_vault["Movies.base"], "views: []", Ct);

        await SyncAsync();

        Read("CLAUDE.md").ShouldBe("# My vault rules");
        Read("Movies.base").ShouldBe("views: []");
    }

    [Fact]
    public async Task Shelfs_own_shared_files_are_brought_up_to_date()
    {
        await File.WriteAllTextAsync(_vault["CLAUDE.md"], "<!-- managed_by: mmdb · old version -->", Ct);
        await File.WriteAllTextAsync(_vault["_Index.md"], "---\nmanaged_by: mmdb\n---\nold", Ct);

        await SyncAsync();

        Read("CLAUDE.md").ShouldContain("## Movie note frontmatter");
        Read("_Index.md").ShouldContain("# Collection index");
    }

    [Fact]
    public async Task A_missing_vault_folder_is_reported_and_not_created()
    {
        var gone = _vault["gone"];

        var error = await Should.ThrowAsync<VaultUnavailableException>(() => _writer.SyncAsync(gone, Films(), [], Ct));

        error.Problem.ShouldBe(VaultProblem.FolderMissing);
        Directory.Exists(gone).ShouldBeFalse();
    }

    [Fact]
    public async Task A_note_that_cant_be_written_is_reported()
    {
        var heat = Film(TestMovies.Heat);
        await WriteAsync(heat);
        heat.Update(TestMovies.Heat with { Rating = "R" }, TestMovies.On(MediaFormat.Dvd), _clock.GetUtcNow());

        // Open in another program that doesn't share it.
        VaultUnavailableException error;
        using (new FileStream(_vault["Movies/Heat (1995).md"], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            error = await Should.ThrowAsync<VaultUnavailableException>(() => WriteAsync(heat));
        }

        error.Problem.ShouldBe(VaultProblem.CannotWrite);
    }
}
