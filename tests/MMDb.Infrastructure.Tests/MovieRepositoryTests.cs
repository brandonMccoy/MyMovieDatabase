using Microsoft.EntityFrameworkCore;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

public sealed class MovieRepositoryTests
{
    private static readonly DateTimeOffset Jan2019 = new(2019, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar2025 = new(2025, 3, 2, 18, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_saved_movie_round_trips_every_field()
    {
        using var database = await TestDatabase.MigratedAsync();
        var details = TestMovies.Heat with { TmdbId = 949, TrailerYouTubeKey = "0xbBLJ1WGwQ", Crew = TestMovies.HeatCrew };
        var saved = Movie.Create(details,
        [
            new(MediaFormat.Uhd4K, Upc: "786936894424"),
            new(MediaFormat.Digital, FilePath: @"D:\Movies\Heat (1995).mkv"),
        ], Jan2019);
        await SaveAsync(database, saved);

        var loaded = await FindAsync(database, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.Title.ShouldBe("Heat");
        loaded.Year.ShouldBe(1995);
        loaded.Directors.ShouldBe(["Michael Mann"]);
        loaded.Cast.ShouldBe([new("Al Pacino", "Vincent Hanna"), new("Robert De Niro", "Neil McCauley")]);
        loaded.Crew.ShouldBe(TestMovies.HeatCrew);
        loaded.Genres.ShouldBe(["Crime", "Drama", "Thriller"]);
        loaded.RuntimeMinutes.ShouldBe(170);
        loaded.Rating.ShouldBe("R");
        loaded.Overview.ShouldBe(details.Overview);
        loaded.TmdbId.ShouldBe(949);
        loaded.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ");
        loaded.CreatedAt.ShouldBe(Jan2019);
        loaded.UpdatedAt.ShouldBe(Jan2019);
        loaded.Version.ShouldBe(1);
        loaded.Formats.Select(f => (f.Format, f.Upc, f.FilePath, f.AddedAt)).ShouldBe(
        [
            (MediaFormat.Uhd4K, Upc.Parse("786936894424"), null, Jan2019),
            (MediaFormat.Digital, null, @"D:\Movies\Heat (1995).mkv", Jan2019),
        ]);
    }

    [Fact]
    public async Task A_movie_with_no_optional_fields_round_trips()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = Movie.Create(TestMovies.Titled("Untitled"), TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, saved);

        var loaded = await FindAsync(database, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.Year.ShouldBeNull();
        loaded.Directors.ShouldBeEmpty();
        loaded.Cast.ShouldBeEmpty();
        loaded.Crew.ShouldBeEmpty();
        loaded.Genres.ShouldBeEmpty();
        loaded.Formats.ShouldHaveSingleItem().Upc.ShouldBeNull();
    }

    [Fact]
    public async Task Updated_formats_are_saved_and_removed_ones_deleted()
    {
        using var database = await TestDatabase.MigratedAsync();
        var movie = Movie.Create(TestMovies.Heat, [new(MediaFormat.Dvd, Upc: "036000291452"), new(MediaFormat.BluRay)], Jan2019);
        await SaveAsync(database, movie);

        await using (var db = database.NewContext())
        {
            var repository = new EfMovieRepository(db);
            var tracked = await repository.FindAsync(movie.Id, Ct);
            tracked!.Update(TestMovies.Heat, [new(MediaFormat.Dvd, Upc: "786936894424"), new(MediaFormat.Uhd4K)], Mar2025);
            await repository.SaveChangesAsync(Ct);
        }

        var loaded = await FindAsync(database, movie.Id);
        loaded!.Formats.Select(f => (f.Format, f.Upc?.Value, f.AddedAt)).ShouldBe(
        [
            (MediaFormat.Uhd4K, null, Mar2025),
            (MediaFormat.Dvd, "786936894424", Jan2019),
        ]);
        loaded.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Changing_only_the_lists_is_saved()
    {
        using var database = await TestDatabase.MigratedAsync();
        var movie = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, movie);

        await using (var db = database.NewContext())
        {
            var repository = new EfMovieRepository(db);
            var tracked = await repository.FindAsync(movie.Id, Ct);
            tracked!.Update(tracked.Details with { Genres = ["Crime"], Cast = [new("Val Kilmer", "Chris Shiherlis")] }, TestMovies.On(MediaFormat.Dvd), Mar2025);
            await repository.SaveChangesAsync(Ct);
        }

        var loaded = await FindAsync(database, movie.Id);
        loaded!.Genres.ShouldBe(["Crime"]);
        loaded.Cast.ShouldBe([new("Val Kilmer", "Chris Shiherlis")]);
    }

    [Fact]
    public async Task Deleting_a_movie_deletes_its_formats()
    {
        using var database = await TestDatabase.MigratedAsync();
        var movie = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd, MediaFormat.BluRay), Jan2019);
        await SaveAsync(database, movie);

        await using (var db = database.NewContext())
        {
            var repository = new EfMovieRepository(db);
            repository.Remove((await repository.FindAsync(movie.Id, Ct))!);
            await repository.SaveChangesAsync(Ct);
        }

        await using var check = database.NewContext();
        (await FindAsync(database, movie.Id)).ShouldBeNull();
        (await check.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM MovieFormats").SingleAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task List_returns_every_movie_with_its_formats()
    {
        using var database = await TestDatabase.MigratedAsync();
        await SaveAsync(database, Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), Jan2019));
        await SaveAsync(database, Movie.Create(TestMovies.Titled("Alien", 1979), TestMovies.On(MediaFormat.Uhd4K, MediaFormat.BluRay), Jan2019));

        await using var db = database.NewContext();
        var all = await new EfMovieRepository(db).ListAsync(Ct);

        all.Select(m => (m.Title, m.Formats.Count)).ShouldBe([("Heat", 1), ("Alien", 2)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_listing_has_what_lists_show_and_sort_by()
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.HeatOnTmdb, [new(MediaFormat.Digital, FilePath: @"D:\Movies\Heat.mkv"), new(MediaFormat.Dvd)], Jan2019);
        heat.AddFormats(TestMovies.On(MediaFormat.Uhd4K), Mar2025);
        heat.UseCover("heat.jpg");
        await SaveAsync(database, heat);

        await using var db = database.NewContext();
        var listing = (await new EfMovieRepository(db).ListAsync(Ct)).ShouldHaveSingleItem();

        listing.ShouldSatisfyAllConditions(
            l => l.Id.ShouldBe(heat.Id),
            l => l.Title.ShouldBe("Heat"),
            l => l.Year.ShouldBe(1995),
            l => l.Directors.ShouldBe(["Michael Mann"]),
            l => l.Genres.ShouldBe(["Crime", "Drama", "Thriller"]),
            l => l.Formats.ShouldBe([MediaFormat.Uhd4K, MediaFormat.Dvd, MediaFormat.Digital]),
            l => l.CreatedAt.ShouldBe(Jan2019),
            l => l.LastAddedAt.ShouldBe(Mar2025),
            l => l.TmdbId.ShouldBe(949),
            l => l.CoverImage.ShouldBe("heat.jpg"));
        listing.ShouldBe(heat.Listing with { Directors = listing.Directors, Genres = listing.Genres, Formats = listing.Formats });
    }

    [Fact]
    public async Task Listing_does_not_read_the_credits()
    {
        using var database = await TestDatabase.MigratedAsync();
        await SaveAsync(database, Movie.Create(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), Jan2019));
        var sql = new List<string>();
        var options = new DbContextOptionsBuilder<ShelfDbContext>(database.Options).LogTo(sql.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]).Options;

        await using var db = new ShelfDbContext(options);
        var repository = new EfMovieRepository(db);
        await repository.ListAsync(Ct);
        await repository.ListByTmdbIdsAsync([949], Ct);
        await repository.FindCreditingAsync("pacino", Ct);

        sql.Count.ShouldBe(3);
        sql.ShouldAllBe(command => !command.Contains("\"Cast\"") && !command.Contains("\"Crew\"") && !command.Contains("\"Overview\"") && !command.Contains("\"TrailerYouTubeKey\""));
    }

    [Fact]
    public async Task Every_film_in_full_comes_with_its_credits()
    {
        using var database = await TestDatabase.MigratedAsync();
        await SaveAsync(database, Movie.Create(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), Jan2019));

        await using var db = database.NewContext();
        var heat = (await new EfMovieRepository(db).ListInFullAsync(Ct)).ShouldHaveSingleItem();

        heat.Cast.ShouldBe(TestMovies.Heat.Cast);
        heat.Crew.ShouldBe(TestMovies.HeatCrew);
    }

    [Theory]
    [InlineData("pacino", "Heat")]
    [InlineData("AL PAC", "Heat")]
    [InlineData("spinotti", "Heat")]
    [InlineData("penelope", "Volver")]
    [InlineData("Penélope Cruz", "Volver")]
    [InlineData("jose luis alcaine", "Volver")]
    public async Task Films_are_found_by_anyone_in_their_credits_ignoring_case_and_accents(string name, string title)
    {
        using var database = await TestDatabase.MigratedAsync();
        await SeedCreditsAsync(database);

        await using var db = database.NewContext();
        var repository = new EfMovieRepository(db);
        var found = await repository.FindCreditingAsync(name, Ct);

        (await repository.ListAsync(Ct)).Where(m => found.Contains(m.Id)).Select(m => m.Title).ShouldBe([title]);
    }

    [Theory]
    [InlineData("Vincent Hanna")]
    [InlineData("Director of Photography")]
    [InlineData("camera")]
    [InlineData("michael mann")]
    [InlineData("nobody")]
    public async Task Parts_played_jobs_departments_and_directors_are_not_credits_to_search(string text)
    {
        using var database = await TestDatabase.MigratedAsync();
        await SeedCreditsAsync(database);

        await using var db = database.NewContext();
        (await new EfMovieRepository(db).FindCreditingAsync(text, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_the_credits_changes_who_a_film_is_found_by()
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, heat);

        await using (var db = database.NewContext())
        {
            var repository = new EfMovieRepository(db);
            var tracked = await repository.FindAsync(heat.Id, Ct);
            tracked!.Update(tracked.Details with { Cast = [new("Val Kilmer", "Chris Shiherlis")] }, TestMovies.On(MediaFormat.Dvd), Mar2025);
            await repository.SaveChangesAsync(Ct);
        }

        await using var fresh = database.NewContext();
        (await new EfMovieRepository(fresh).FindCreditingAsync("kilmer", Ct)).ShouldBe([heat.Id]);
        (await new EfMovieRepository(fresh).FindCreditingAsync("pacino", Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Films_saved_before_credits_could_be_searched_are_indexed_once_on_migrating()
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, heat);
        await using (var db = database.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE Movies SET People = NULL", Ct);
            (await new EfMovieRepository(db).FindCreditingAsync("pacino", Ct)).ShouldBeEmpty();
        }

        await using (var db = database.NewContext())
        {
            await db.IndexPeopleAsync(Ct);
        }

        await using var fresh = database.NewContext();
        (await new EfMovieRepository(fresh).FindCreditingAsync("pacino", Ct)).ShouldBe([heat.Id]);
        var loaded = await new EfMovieRepository(fresh).FindAsync(heat.Id, Ct);
        loaded!.Version.ShouldBe(heat.Version);
        loaded.UpdatedAt.ShouldBe(heat.UpdatedAt);
    }

    private static async Task SeedCreditsAsync(TestDatabase database)
    {
        await SaveAsync(database, Movie.Create(
            TestMovies.Heat with { Crew = [new("Dante Spinotti", "Director of Photography", "Camera")] }, TestMovies.On(MediaFormat.Dvd), Jan2019));
        await SaveAsync(database, Movie.Create(
            TestMovies.Titled("Volver", 2006) with
            {
                Directors = ["Pedro Almodóvar"],
                Cast = [new("Penélope Cruz", "Raimunda")],
                Crew = [new("José Luis Alcaine", "Director of Photography", "Camera")],
            },
            TestMovies.On(MediaFormat.Dvd),
            Jan2019));
    }

    [Fact]
    public async Task Saving_a_stale_copy_raises_a_concurrency_conflict()
    {
        using var database = await TestDatabase.MigratedAsync();
        var movie = Movie.Create(TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, movie);

        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var firstRepository = new EfMovieRepository(first);
        var secondRepository = new EfMovieRepository(second);
        var firstCopy = await firstRepository.FindAsync(movie.Id, Ct);
        var secondCopy = await secondRepository.FindAsync(movie.Id, Ct);

        firstCopy!.Update(TestMovies.Heat with { Title = "Heat (first)" }, TestMovies.On(MediaFormat.Dvd), Mar2025);
        await firstRepository.SaveChangesAsync(Ct);
        secondCopy!.Update(TestMovies.Heat with { Title = "Heat (second)" }, TestMovies.On(MediaFormat.Dvd), Mar2025);

        await Should.ThrowAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(Ct));
        (await FindAsync(database, movie.Id))!.Title.ShouldBe("Heat (first)");
    }

    [Fact]
    public async Task The_poster_and_cover_are_saved()
    {
        using var database = await TestDatabase.MigratedAsync();
        var movie = Movie.Create(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), Jan2019);
        movie.UseCover("abc-heat.jpg");
        await SaveAsync(database, movie);

        var loaded = await FindAsync(database, movie.Id);

        loaded!.PosterPath.ShouldBe("/heat.jpg");
        loaded.CoverImage.ShouldBe("abc-heat.jpg");
    }

    [Theory]
    [InlineData("786936894424")]
    [InlineData("0786936894424")]
    public async Task A_film_is_found_by_its_barcode_in_either_form(string barcode)
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.Heat, [new(MediaFormat.Dvd), new(MediaFormat.Uhd4K, Upc: "786936894424")], Jan2019);
        await SaveAsync(database, Movie.Create(TestMovies.Titled("Alien"), [new(MediaFormat.BluRay, Upc: "036000291452")], Jan2019));
        await SaveAsync(database, heat);

        await using var db = database.NewContext();
        var found = await new EfMovieRepository(db).FindByUpcAsync(Upc.Parse(barcode), Ct);

        found.ShouldNotBeNull().Id.ShouldBe(heat.Id);
    }

    [Fact]
    public async Task An_unknown_barcode_finds_nothing()
    {
        using var database = await TestDatabase.MigratedAsync();
        await SaveAsync(database, Movie.Create(TestMovies.Heat, [new(MediaFormat.Dvd, Upc: "786936894424")], Jan2019));

        await using var db = database.NewContext();
        (await new EfMovieRepository(db).FindByUpcAsync(Upc.Parse("036000291452"), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Films_are_found_by_tmdb_id()
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), Jan2019);
        var alien = Movie.Create(TestMovies.Titled("Alien") with { TmdbId = 348 }, TestMovies.On(MediaFormat.Dvd), Jan2019);
        await SaveAsync(database, heat);
        await SaveAsync(database, alien);
        await SaveAsync(database, Movie.Create(TestMovies.Titled("Jaws"), TestMovies.On(MediaFormat.Dvd), Jan2019));

        await using var db = database.NewContext();
        var repository = new EfMovieRepository(db);
        (await repository.FindByTmdbIdAsync(949, Ct)).ShouldNotBeNull().Id.ShouldBe(heat.Id);
        (await repository.FindByTmdbIdAsync(1, Ct)).ShouldBeNull();
        (await repository.ListByTmdbIdsAsync([949, 348, 1], Ct)).Select(m => m.Title).Order().ShouldBe(["Alien", "Heat"]);
    }

    [Fact]
    public async Task Digital_copies_are_listed_with_their_file_and_the_films_formats()
    {
        using var database = await TestDatabase.MigratedAsync();
        var heat = Movie.Create(TestMovies.HeatOnTmdb, [new(MediaFormat.BluRay), new(MediaFormat.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")], Jan2019);
        heat.UseCover("heat.jpg");
        var alien = Movie.Create(TestMovies.Titled("Alien", 1979), [new(MediaFormat.Digital)], Jan2019);
        var disc = Movie.Create(TestMovies.Titled("Arrival", 2016), [new(MediaFormat.Dvd)], Jan2019);
        await SaveAsync(database, heat);
        await SaveAsync(database, alien);
        await SaveAsync(database, disc);

        await using var db = database.NewContext();
        var copies = (await new EfMovieRepository(db).ListDigitalAsync(Ct)).OrderBy(c => c.Title).ToList();

        copies.Select(c => (c.MovieId, c.Title, c.Year, c.FilePath, c.TmdbId, c.CoverImage)).ShouldBe(
        [
            (alien.Id, "Alien", 1979, null, null, null),
            (heat.Id, "Heat", 1995, @"D:\Movies\Heat (1995).mkv", 949, "heat.jpg"),
        ]);
        copies[0].Formats.ShouldBe([MediaFormat.Digital]);
        copies[1].Formats.ShouldBe([MediaFormat.BluRay, MediaFormat.Digital]);
        db.ChangeTracker.Entries().ShouldBeEmpty();
    }

    private static async Task SaveAsync(TestDatabase database, Movie movie)
    {
        await using var db = database.NewContext();
        var repository = new EfMovieRepository(db);
        repository.Add(movie);
        await repository.SaveChangesAsync(Ct);
    }

    private static async Task<Movie?> FindAsync(TestDatabase database, Guid id)
    {
        await using var db = database.NewContext();
        return await new EfMovieRepository(db).FindAsync(id, Ct);
    }
}
