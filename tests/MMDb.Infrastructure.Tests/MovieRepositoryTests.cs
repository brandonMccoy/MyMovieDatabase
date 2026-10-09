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
        var details = TestMovies.Heat with { TmdbId = 949, TrailerYouTubeKey = "0xbBLJ1WGwQ" };
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
