using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Domain;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Server.Tests;

public sealed class InProcessCollectionApiTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Vincent Hanna")],
        Genres = ["Crime", "Drama"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "A detective and a thief.",
        Formats = [new(MovieFormatKind.Uhd4K, Upc: "786936894424"), new(MovieFormatKind.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")],
    };

    public async ValueTask InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddShelf(_sqlite.ConnectionString)
            .AddSingleton<TimeProvider>(_clock)
            .BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task An_added_movie_comes_back_with_every_field()
    {
        var response = await Api.AddMovieAsync(Heat, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var movie = await Api.GetMovieAsync(response.Movie!.Id, Ct);
        movie.ShouldNotBeNull();
        movie.Title.ShouldBe("Heat");
        movie.Year.ShouldBe(1995);
        movie.Directors.ShouldBe(["Michael Mann"]);
        movie.Cast.ShouldBe([new CastMemberDto("Al Pacino", "Vincent Hanna")]);
        movie.Genres.ShouldBe(["Crime", "Drama"]);
        movie.RuntimeMinutes.ShouldBe(170);
        movie.Rating.ShouldBe("R");
        movie.Overview.ShouldBe("A detective and a thief.");
        movie.Version.ShouldBe(1);
        movie.CreatedAt.ShouldBe(Start);
        movie.TrailerUrl.AbsoluteUri.ShouldBe("https://www.youtube.com/results?search_query=Heat%201995%20trailer");
        movie.Formats.ShouldBe(
        [
            new MovieFormatDto(MovieFormatKind.Uhd4K, "786936894424", null, Start),
            new MovieFormatDto(MovieFormatKind.Digital, null, @"D:\Movies\Heat (1995).mkv", Start),
        ]);
    }

    [Fact]
    public async Task A_stored_trailer_key_links_to_the_video()
    {
        var response = await Api.AddMovieAsync(Heat with { TrailerYouTubeKey = "0xbBLJ1WGwQ", TmdbId = 949 }, Ct);

        response.Movie!.TrailerUrl.ShouldBe(new Uri("https://www.youtube.com/watch?v=0xbBLJ1WGwQ"));
        response.Movie.TmdbId.ShouldBe(949);
    }

    [Fact]
    public async Task Invalid_input_reports_errors_keyed_by_field()
    {
        var response = await Api.AddMovieAsync(Heat with { Title = "", Formats = [new(MovieFormatKind.BluRay, Upc: "123")] }, Ct);

        response.Status.ShouldBe(SaveStatus.Invalid);
        response.Movie.ShouldBeNull();
        response.Errors.Select(e => e.Field).ShouldBe(["Title", $"{MovieFormatKind.BluRay}.Upc"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_unknown_format_is_reported_not_thrown()
    {
        var response = await Api.AddMovieAsync(Heat with { Formats = [new((MovieFormatKind)42)] }, Ct);

        response.Status.ShouldBe(SaveStatus.Invalid);
        response.Errors.ShouldHaveSingleItem().Field.ShouldBe("Formats");
    }

    [Fact]
    public async Task Null_lists_from_the_wire_are_treated_as_empty()
    {
        var response = await Api.AddMovieAsync(Heat with { Directors = null!, Cast = null!, Genres = null! }, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        response.Movie!.Directors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_saves_and_returns_the_new_version()
    {
        var added = (await Api.AddMovieAsync(Heat, Ct)).Movie!;
        _clock.Advance(TimeSpan.FromDays(1));

        var response = await Api.UpdateMovieAsync(added.Id, added.Version, Heat with { Title = "Heat (1995)", Formats = [new(MovieFormatKind.Dvd)] }, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        response.Movie!.Version.ShouldBe(2);
        var movie = await Api.GetMovieAsync(added.Id, Ct);
        movie!.Title.ShouldBe("Heat (1995)");
        movie.Formats.ShouldBe([new MovieFormatDto(MovieFormatKind.Dvd, null, null, Start.AddDays(1))]);
    }

    [Fact]
    public async Task Update_with_an_old_version_is_a_conflict()
    {
        var added = (await Api.AddMovieAsync(Heat, Ct)).Movie!;
        await Api.UpdateMovieAsync(added.Id, added.Version, Heat with { Title = "Newer" }, Ct);

        var response = await Api.UpdateMovieAsync(added.Id, added.Version, Heat with { Title = "Stale" }, Ct);

        response.Status.ShouldBe(SaveStatus.Conflict);
    }

    [Fact]
    public async Task Update_of_a_missing_movie_is_not_found()
    {
        (await Api.UpdateMovieAsync(Guid.NewGuid(), 1, Heat, Ct)).Status.ShouldBe(SaveStatus.NotFound);
    }

    [Fact]
    public async Task List_returns_posters_and_facets()
    {
        await Api.AddMovieAsync(Heat, Ct);
        await Api.AddMovieAsync(Heat with { Title = "Alien", Year = 1979, Directors = ["Ridley Scott"], Genres = ["Horror"], Formats = [new(MovieFormatKind.BluRay)] }, Ct);

        var list = await Api.ListMoviesAsync(new() { Format = MovieFormatKind.Uhd4K, Sort = MovieSortOrder.Title }, Ct);

        var heat = list.Movies.ShouldHaveSingleItem();
        heat.Title.ShouldBe("Heat");
        heat.Year.ShouldBe(1995);
        heat.Directors.ShouldBe(["Michael Mann"]);
        heat.Formats.ShouldBe([MovieFormatKind.Uhd4K, MovieFormatKind.Digital]);
        heat.TrailerUrl.AbsoluteUri.ShouldContain("Heat%201995%20trailer");
        list.Facets.FilmCount.ShouldBe(2);
        list.Facets.DiscCount.ShouldBe(2);
        list.Facets.MatchingCount.ShouldBe(2);
        list.Facets.FormatCounts[MovieFormatKind.BluRay].ShouldBe(1);
        list.Facets.Genres.ShouldBe(["Crime", "Drama", "Horror"]);
        list.Facets.Decades.ShouldBe([1990, 1970]);
    }

    [Fact]
    public async Task A_blank_genre_filter_means_any_genre()
    {
        await Api.AddMovieAsync(Heat, Ct);

        (await Api.ListMoviesAsync(new() { Genre = "" }, Ct)).Movies.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_removes_the_movie()
    {
        var added = (await Api.AddMovieAsync(Heat, Ct)).Movie!;

        (await Api.DeleteMovieAsync(added.Id, Ct)).ShouldBeTrue();

        (await Api.GetMovieAsync(added.Id, Ct)).ShouldBeNull();
        (await Api.DeleteMovieAsync(added.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public void Contract_formats_match_the_domain_formats_by_name_and_order()
    {
        Enum.GetNames<MovieFormatKind>().ShouldBe(Enum.GetNames<MediaFormat>());
    }

    [Fact]
    public void Contract_sort_orders_cover_every_domain_sort()
    {
        Enum.GetNames<MovieSortOrder>().ShouldBe(Enum.GetNames<Application.Movies.MovieSort>());
    }
}
