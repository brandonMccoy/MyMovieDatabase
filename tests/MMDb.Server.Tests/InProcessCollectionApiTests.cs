using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Metadata;
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
    private readonly FakeOnline _online = new();
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Vincent Hanna")],
        Crew = [new("Michael Mann", "Screenplay", "Writing"), new("Dante Spinotti", "Director of Photography", "Camera")],
        Genres = ["Crime", "Drama"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "A detective and a thief.",
        Formats = [new(MovieFormatKind.Uhd4K, Upc: "786936894424"), new(MovieFormatKind.Digital, FilePath: @"D:\Movies\Heat (1995).mkv")],
    };

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(_clock);
        _services = _online.ReplaceIn(services).BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task Whatever_changes_the_collection_is_announced_and_nothing_else_is()
    {
        var changes = 0;
        Api.Changed += () => changes++;

        var added = (await Api.AddMovieAsync(Heat, Ct)).Movie!;
        changes.ShouldBe(1);

        await Api.AddMovieAsync(Heat with { Title = "" }, Ct);
        await Api.UpdateMovieAsync(added.Id, added.Version + 5, Heat, Ct);
        await Api.UpdateMovieAsync(Guid.NewGuid(), 1, Heat, Ct);
        await Api.DeleteMovieAsync(Guid.NewGuid(), Ct);
        await Api.ListMoviesAsync(new(), Ct);
        await Api.GetMovieAsync(added.Id, Ct);
        await Api.LookupMovieAsync("heat", Ct);
        changes.ShouldBe(1, "refused saves and reads change nothing");

        await Api.UpdateMovieAsync(added.Id, added.Version, Heat with { Rating = "18" }, Ct);
        changes.ShouldBe(2);
        await Api.DeleteMovieAsync(added.Id, Ct);
        changes.ShouldBe(3);

        var comic = (await Api.AddComicAsync(new() { Series = "Saga", Number = "7" }, Ct)).Comic!;
        await Api.AddComicAsync(new() { Series = "Saga", Number = "" }, Ct);
        await Api.UpdateComicAsync(comic.Id, comic.Version, new() { Series = "Saga", Number = "8" }, Ct);
        await Api.DeleteComicAsync(comic.Id, Ct);
        await Api.DeleteComicAsync(comic.Id, Ct);
        changes.ShouldBe(6);
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
        movie.Crew.ShouldBe([new CrewMemberDto("Michael Mann", "Screenplay", "Writing"), new CrewMemberDto("Dante Spinotti", "Director of Photography", "Camera")]);
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
        var response = await Api.AddMovieAsync(Heat with { Directors = null!, Cast = null!, Crew = null!, Genres = null! }, Ct);

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

        var list = await Api.ListMoviesAsync(new() { Formats = [MovieFormatKind.Uhd4K], Sort = MovieSortOrder.Title }, Ct);

        var heat = list.Movies.ShouldHaveSingleItem();
        heat.Title.ShouldBe("Heat");
        heat.Year.ShouldBe(1995);
        heat.Directors.ShouldBe(["Michael Mann"]);
        heat.Formats.ShouldBe([MovieFormatKind.Uhd4K, MovieFormatKind.Digital]);
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

        (await Api.ListMoviesAsync(new() { Genres = [""] }, Ct)).Movies.Count.ShouldBe(1);
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

    [Fact]
    public async Task A_lookup_returns_candidates_ready_for_the_add_screen()
    {
        _online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.HeatAndDustOnTmdb);

        var response = await Api.LookupMovieAsync("heat", Ct);

        response.Status.ShouldBe(LookupStatus.Found);
        response.Candidates.Select(c => c.Title).ShouldBe(["Heat", "Heat and Dust"]);
        var heat = response.Candidates[0];
        heat.TmdbId.ShouldBe(949);
        heat.Year.ShouldBe(1995);
        heat.Directors.ShouldBe(["Michael Mann"]);
        heat.Cast[0].ShouldBe(new CastMemberDto("Al Pacino", "Vincent Hanna"));
        heat.Crew.Count.ShouldBe(TestMovies.HeatCrew.Count);
        heat.Crew[1].ShouldBe(new CrewMemberDto("Michael Mann", "Screenplay", "Writing"));
        heat.PosterPath.ShouldBe("/heat.jpg");
        heat.PosterUrl.ShouldBe(new Uri("https://image.tmdb.org/t/p/w342/heat.jpg"));
        heat.ThumbnailUrl.ShouldBe(new Uri("https://image.tmdb.org/t/p/w92/heat.jpg"));
        response.Candidates[1].PosterUrl.ShouldBeNull();
    }

    [Fact]
    public async Task A_barcode_lookup_reports_the_product_and_its_format()
    {
        _online.Tmdb.Add(TestMovies.HeatOnTmdb);
        _online.Barcodes.Add("786936894424", "Heat [Blu-ray]");

        var response = await Api.LookupMovieAsync("786936894424", Ct);

        response.Upc.ShouldBe("786936894424");
        response.ProductName.ShouldBe("Heat [Blu-ray]");
        response.ProductTitle.ShouldBe("Heat");
        response.Format.ShouldBe(MovieFormatKind.BluRay);
        response.Candidates.ShouldHaveSingleItem().Owned.ShouldBeNull();
    }

    [Fact]
    public async Task A_lookup_points_to_films_already_owned()
    {
        _online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var owned = await Api.AddMovieAsync(Heat with { TmdbId = 949, Formats = [new(MovieFormatKind.Dvd, Upc: "786936894424")] }, Ct);

        var byTitle = await Api.LookupMovieAsync("heat", Ct);
        var byBarcode = await Api.LookupMovieAsync("786936894424", Ct);

        byTitle.Candidates.ShouldHaveSingleItem().Owned.ShouldNotBeNull().ShouldSatisfyAllConditions(
            o => o.Id.ShouldBe(owned.Movie!.Id),
            o => o.Formats.ShouldBe([MovieFormatKind.Dvd]));
        byBarcode.Status.ShouldBe(LookupStatus.BarcodeOwned);
        byBarcode.OwnedByBarcode.ShouldNotBeNull().Id.ShouldBe(owned.Movie!.Id);
    }

    [Theory]
    [InlineData(MetadataProblem.NotConfigured, LookupStatus.NotConfigured)]
    [InlineData(MetadataProblem.KeyRejected, LookupStatus.KeyRejected)]
    [InlineData(MetadataProblem.Busy, LookupStatus.Busy)]
    [InlineData(MetadataProblem.Unavailable, LookupStatus.Unavailable)]
    public async Task Lookup_problems_are_reported_with_the_service(MetadataProblem problem, LookupStatus status)
    {
        _online.Tmdb.FailWith = problem;

        var response = await Api.LookupMovieAsync("heat", Ct);

        response.Status.ShouldBe(status);
        response.FailedService.ShouldBe("TMDB");
    }

    [Fact]
    public async Task A_film_saved_from_a_lookup_has_its_cover()
    {
        var response = await Api.AddMovieAsync(Heat with { TmdbId = 949, PosterPath = "/heat.jpg" }, Ct);

        var movie = response.Movie!;
        movie.PosterPath.ShouldBe("/heat.jpg");
        movie.CoverUrl.ShouldBe(new Uri($"https://covers.test/{movie.Id:N}-heat.jpg"));
        (await Api.ListMoviesAsync(new(), Ct)).Movies.ShouldHaveSingleItem().CoverUrl.ShouldBe(movie.CoverUrl);
    }

    [Fact]
    public async Task A_film_without_a_cover_has_no_cover_url()
    {
        var response = await Api.AddMovieAsync(Heat, Ct);

        response.Movie!.CoverUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Adding_a_film_already_owned_on_that_format_says_so()
    {
        var owned = await Api.AddMovieAsync(Heat with { TmdbId = 949 }, Ct);

        var again = await Api.AddMovieAsync(Heat with { TmdbId = 949, Formats = [new(MovieFormatKind.Uhd4K)] }, Ct);

        again.Status.ShouldBe(SaveStatus.AlreadyOwned);
        again.Movie!.Id.ShouldBe(owned.Movie!.Id);
    }

    [Fact]
    public async Task A_bad_poster_path_is_rejected_as_a_field_error()
    {
        var response = await Api.AddMovieAsync(Heat with { PosterPath = "../../evil.jpg" }, Ct);

        response.Status.ShouldBe(SaveStatus.Invalid);
        response.Errors.ShouldHaveSingleItem().Field.ShouldBe("PosterPath");
    }
}
