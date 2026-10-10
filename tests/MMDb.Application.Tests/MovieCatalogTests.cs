using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Testing;

namespace MMDb.Application.Tests;

public sealed class MovieCatalogTests
{
    [Fact]
    public async Task An_added_movie_is_saved_with_its_formats()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var result = await shelf.Movies((catalog, ct) =>
            catalog.AddAsync(TestMovies.Heat, [new(MediaFormat.Uhd4K, Upc: "786936894424"), new(MediaFormat.Dvd)], ct));

        var added = result.ShouldBeOfType<SaveMovieResult.Saved>().Movie;
        var saved = await shelf.GetAsync(added.Id);
        saved.ShouldNotBeNull();
        saved.Title.ShouldBe("Heat");
        saved.Cast.ShouldBe(TestMovies.Heat.Cast);
        saved.Crew.ShouldBe(TestMovies.Heat.Crew);
        saved.Formats.Select(f => f.Format).ShouldBe([MediaFormat.Uhd4K, MediaFormat.Dvd]);
        saved.Formats[0].Upc?.Value.ShouldBe("786936894424");
    }

    [Fact]
    public async Task Adding_stamps_the_movie_and_its_formats_with_the_current_time()
    {
        await using var shelf = await TestShelf.CreateAsync();
        shelf.Clock.Advance(TimeSpan.FromDays(3));

        var added = await shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        var saved = await shelf.GetAsync(added.Id);
        saved!.CreatedAt.ShouldBe(TestShelf.Start.AddDays(3));
        saved.Formats[0].AddedAt.ShouldBe(TestShelf.Start.AddDays(3));
    }

    [Fact]
    public async Task Adding_invalid_input_returns_the_errors_and_saves_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var result = await shelf.Movies((catalog, ct) =>
            catalog.AddAsync(TestMovies.Heat with { Title = " ", Year = 1700 }, [new(MediaFormat.Dvd, Upc: "123")], ct));

        result.ShouldBeOfType<SaveMovieResult.Invalid>().Errors.Select(e => e.Field)
            .ShouldBe([MovieFields.Title, MovieFields.Year, "Dvd.Upc"], ignoreOrder: true);
        (await shelf.ListAsync()).Facets.FilmCount.ShouldBe(0);
    }

    [Fact]
    public async Task Adding_a_movie_without_a_format_is_rejected()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var result = await shelf.Movies((catalog, ct) => catalog.AddAsync(TestMovies.Heat, [], ct));

        result.ShouldBeOfType<SaveMovieResult.Invalid>().Errors.ShouldHaveSingleItem().Field.ShouldBe(MovieFields.Formats);
    }

    [Fact]
    public async Task An_update_is_saved_and_bumps_the_version()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(30));

        var result = await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(added.Id, added.Version, TestMovies.Heat with { RuntimeMinutes = 171 }, TestMovies.On(MediaFormat.Dvd, MediaFormat.Uhd4K), ct));

        result.ShouldBeOfType<SaveMovieResult.Saved>();
        var saved = await shelf.GetAsync(added.Id);
        saved!.RuntimeMinutes.ShouldBe(171);
        saved.Version.ShouldBe(added.Version + 1);
        saved.UpdatedAt.ShouldBe(TestShelf.Start.AddDays(30));
        saved.Formats.Select(f => (f.Format, f.AddedAt)).ShouldBe(
            [(MediaFormat.Uhd4K, TestShelf.Start.AddDays(30)), (MediaFormat.Dvd, TestShelf.Start)]);
    }

    [Fact]
    public async Task Updating_a_movie_that_does_not_exist_is_not_found()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var result = await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(Guid.NewGuid(), 1, TestMovies.Heat, TestMovies.On(MediaFormat.Dvd), ct));

        result.ShouldBeOfType<SaveMovieResult.NotFound>();
    }

    [Fact]
    public async Task Updating_from_a_stale_version_is_a_conflict_and_keeps_the_newer_change()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(added.Id, added.Version, TestMovies.Heat with { Title = "Heat (newer)" }, TestMovies.On(MediaFormat.Dvd), ct));

        var result = await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(added.Id, added.Version, TestMovies.Heat with { Title = "Heat (stale)" }, TestMovies.On(MediaFormat.Dvd), ct));

        result.ShouldBeOfType<SaveMovieResult.Conflict>();
        (await shelf.GetAsync(added.Id))!.Title.ShouldBe("Heat (newer)");
    }

    [Fact]
    public async Task An_invalid_update_returns_the_errors_and_changes_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        var result = await shelf.Movies((catalog, ct) =>
            catalog.UpdateAsync(added.Id, added.Version, TestMovies.Heat with { Title = "" }, [], ct));

        result.ShouldBeOfType<SaveMovieResult.Invalid>().Errors.Select(e => e.Field)
            .ShouldBe([MovieFields.Title, MovieFields.Formats], ignoreOrder: true);
        var saved = await shelf.GetAsync(added.Id);
        saved!.Title.ShouldBe("Heat");
        saved.Version.ShouldBe(added.Version);
        saved.Owns(MediaFormat.Dvd).ShouldBeTrue();
    }

    [Fact]
    public async Task A_deleted_movie_is_gone()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var added = await shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);

        var deleted = await shelf.Movies((catalog, ct) => catalog.DeleteAsync(added.Id, ct));

        deleted.ShouldBeTrue();
        (await shelf.GetAsync(added.Id)).ShouldBeNull();
        (await shelf.ListAsync()).Movies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_movie_that_does_not_exist_reports_false()
    {
        await using var shelf = await TestShelf.CreateAsync();

        (await shelf.Movies((catalog, ct) => catalog.DeleteAsync(Guid.NewGuid(), ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Getting_a_movie_that_does_not_exist_returns_null()
    {
        await using var shelf = await TestShelf.CreateAsync();

        (await shelf.GetAsync(Guid.NewGuid())).ShouldBeNull();
    }
}
