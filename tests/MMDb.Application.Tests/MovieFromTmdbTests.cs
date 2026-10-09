using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>Saving films picked from a lookup: covers, and films already owned.</summary>
public sealed class MovieFromTmdbTests
{
    private static async Task<Movie> AddAsync(TestShelf shelf, MovieDetails details, params MediaFormat[] formats) =>
        (await shelf.Movies((catalog, ct) => catalog.AddAsync(details, TestMovies.On(formats), ct))).ShouldBeOfType<SaveMovieResult.Saved>().Movie;

    private static Task<SaveMovieResult> UpdateAsync(TestShelf shelf, Movie movie, MovieDetails details) =>
        shelf.Movies((catalog, ct) => catalog.UpdateAsync(movie.Id, movie.Version, details, TestMovies.On(MediaFormat.Dvd), ct));

    [Fact]
    public async Task Adding_a_film_with_a_poster_downloads_and_keeps_its_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        var saved = await shelf.GetAsync(added.Id);
        saved!.PosterPath.ShouldBe("/heat.jpg");
        saved.CoverImage.ShouldBe($"{added.Id:N}-heat.jpg");
        shelf.Online.Covers.Files[saved.CoverImage!].ShouldBe(FakePosters.ImageFor("/heat.jpg"));
    }

    [Fact]
    public async Task A_film_without_a_poster_has_no_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var added = await AddAsync(shelf, TestMovies.Heat, MediaFormat.Dvd);

        (await shelf.GetAsync(added.Id))!.CoverImage.ShouldBeNull();
        shelf.Online.Posters.Fetched.ShouldBeEmpty();
    }

    [Fact]
    public async Task Offline_the_film_is_saved_without_a_cover_and_the_next_save_fetches_it()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Online.Posters.Offline = true;
        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        (await shelf.GetAsync(added.Id))!.CoverImage.ShouldBeNull();

        shelf.Online.Posters.Offline = false;
        await UpdateAsync(shelf, added, TestMovies.HeatOnTmdb with { Rating = "R" });

        (await shelf.GetAsync(added.Id))!.CoverImage.ShouldBe($"{added.Id:N}-heat.jpg");
    }

    [Fact]
    public async Task Saving_again_with_the_same_poster_downloads_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        await UpdateAsync(shelf, added, TestMovies.HeatOnTmdb with { Rating = "PG" });

        shelf.Online.Posters.Fetched.ShouldBe(["/heat.jpg"]);
    }

    [Fact]
    public async Task A_new_poster_replaces_the_old_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        await UpdateAsync(shelf, added, TestMovies.HeatOnTmdb with { PosterPath = "/heat-4k.jpg" });

        (await shelf.GetAsync(added.Id))!.CoverImage.ShouldBe($"{added.Id:N}-heat-4k.jpg");
        shelf.Online.Covers.Files.Keys.ShouldBe([$"{added.Id:N}-heat-4k.jpg"]);
    }

    [Fact]
    public async Task Removing_the_poster_removes_the_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        await UpdateAsync(shelf, added, TestMovies.HeatOnTmdb with { PosterPath = null });

        (await shelf.GetAsync(added.Id))!.CoverImage.ShouldBeNull();
        shelf.Online.Covers.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_film_removes_its_cover()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        (await shelf.Movies((catalog, ct) => catalog.DeleteAsync(added.Id, ct))).ShouldBeTrue();

        shelf.Online.Covers.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_conflicting_update_downloads_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await AddAsync(shelf, TestMovies.Heat, MediaFormat.Dvd);
        await UpdateAsync(shelf, added, TestMovies.Heat with { Rating = "PG" });

        var result = await UpdateAsync(shelf, added, TestMovies.HeatOnTmdb);

        result.ShouldBeOfType<SaveMovieResult.Conflict>();
        shelf.Online.Posters.Fetched.ShouldBeEmpty();
        shelf.Online.Covers.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Adding_a_film_already_owned_adds_the_new_format_to_it_instead()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var owned = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(30));

        var result = await shelf.Movies((catalog, ct) => catalog.AddAsync(
            TestMovies.HeatOnTmdb with { Rating = "PG-13" }, [new(MediaFormat.Uhd4K, Upc: "786936894424")], ct));

        result.ShouldBeOfType<SaveMovieResult.Saved>().Movie.Id.ShouldBe(owned.Id);
        var list = await shelf.ListAsync();
        var film = list.Movies.ShouldHaveSingleItem();
        film.Formats.Select(f => (f.Format, f.AddedAt)).ShouldBe([(MediaFormat.Uhd4K, TestShelf.Start.AddDays(30)), (MediaFormat.Dvd, TestShelf.Start)]);
        film.Rating.ShouldBe("R");
    }

    [Fact]
    public async Task Adding_a_film_already_owned_on_that_format_reports_it_and_changes_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var owned = await AddAsync(shelf, TestMovies.HeatOnTmdb, MediaFormat.Dvd);

        var result = await shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.Dvd), ct));

        result.ShouldBeOfType<SaveMovieResult.AlreadyOwned>().Movie.Id.ShouldBe(owned.Id);
        (await shelf.GetAsync(owned.Id))!.Version.ShouldBe(owned.Version);
    }
}
