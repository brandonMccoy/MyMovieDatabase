using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class MovieTests
{
    private static readonly DateTimeOffset Jan2019 = new(2019, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar2025 = new(2025, 3, 2, 18, 30, 0, TimeSpan.Zero);

    private static MovieDetails Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Vincent Hanna"), new("Robert De Niro", "Neil McCauley")],
        Genres = ["Crime", "Drama", "Thriller"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "A detective and a thief circle each other across Los Angeles.",
    };

    [Fact]
    public void Creating_a_movie_keeps_its_details_and_formats()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd, Upc: "786936894424")], Jan2019);

        movie.Title.ShouldBe("Heat");
        movie.Year.ShouldBe(1995);
        movie.RuntimeMinutes.ShouldBe(170);
        movie.Rating.ShouldBe("R");
        movie.Overview.ShouldBe(Heat.Overview);
        movie.Directors.ShouldBe(["Michael Mann"]);
        movie.Cast.ShouldBe(Heat.Cast);
        movie.Genres.ShouldBe(["Crime", "Drama", "Thriller"]);
        movie.Formats.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            f => f.Format.ShouldBe(MediaFormat.Dvd),
            f => f.Upc.ShouldBe(Upc.Parse("786936894424")),
            f => f.AddedAt.ShouldBe(Jan2019));
    }

    [Fact]
    public void A_new_movie_has_an_id_timestamps_and_version_one()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd)], Jan2019);

        movie.Id.ShouldNotBe(Guid.Empty);
        movie.CreatedAt.ShouldBe(Jan2019);
        movie.UpdatedAt.ShouldBe(Jan2019);
        movie.Version.ShouldBe(1);
    }

    [Fact]
    public void Creating_normalizes_the_input()
    {
        var movie = Movie.Create(Heat with { Title = " Heat  ", Rating = "" }, [new(MediaFormat.Digital, FilePath: @" D:\Movies\Heat.mkv ")], Jan2019);

        movie.Title.ShouldBe("Heat");
        movie.Rating.ShouldBeNull();
        movie.Formats[0].FilePath.ShouldBe(@"D:\Movies\Heat.mkv");
    }

    [Fact]
    public void Creating_with_invalid_input_throws_with_every_error()
    {
        var ex = Should.Throw<DomainValidationException>(() => Movie.Create(Heat with { Title = "" }, [], Jan2019));

        ex.Errors.Select(e => e.Field).ShouldBe([MovieFields.Title, MovieFields.Formats], ignoreOrder: true);
    }

    [Fact]
    public void Formats_are_listed_in_display_order()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Digital), new(MediaFormat.Dvd), new(MediaFormat.Uhd4K), new(MediaFormat.BluRay)], Jan2019);

        movie.Formats.Select(f => f.Format).ShouldBe([MediaFormat.Uhd4K, MediaFormat.BluRay, MediaFormat.Dvd, MediaFormat.Digital]);
    }

    [Fact]
    public void Updating_replaces_the_details_and_bumps_the_version()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd)], Jan2019);

        movie.Update(Heat with { Title = "Heat (Director's Definitive Edition)", RuntimeMinutes = 171 }, [new(MediaFormat.Dvd)], Mar2025);

        movie.Title.ShouldBe("Heat (Director's Definitive Edition)");
        movie.RuntimeMinutes.ShouldBe(171);
        movie.Version.ShouldBe(2);
        movie.UpdatedAt.ShouldBe(Mar2025);
        movie.CreatedAt.ShouldBe(Jan2019);
    }

    [Fact]
    public void Adding_a_format_on_update_stamps_it_with_the_update_time()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd)], Jan2019);

        movie.Update(Heat, [new(MediaFormat.Dvd), new(MediaFormat.Uhd4K)], Mar2025);

        movie.Formats.Select(f => (f.Format, f.AddedAt)).ShouldBe([(MediaFormat.Uhd4K, Mar2025), (MediaFormat.Dvd, Jan2019)]);
        movie.LastAddedAt.ShouldBe(Mar2025);
    }

    [Fact]
    public void A_kept_format_keeps_its_added_date_but_takes_the_new_barcode()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd, Upc: "036000291452")], Jan2019);

        movie.Update(Heat, [new(MediaFormat.Dvd, Upc: "786936894424")], Mar2025);

        var dvd = movie.Formats.ShouldHaveSingleItem();
        dvd.AddedAt.ShouldBe(Jan2019);
        dvd.Upc.ShouldBe(Upc.Parse("786936894424"));
    }

    [Fact]
    public void Formats_left_out_of_an_update_are_removed()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd), new(MediaFormat.BluRay)], Jan2019);

        movie.Update(Heat, [new(MediaFormat.BluRay)], Mar2025);

        movie.Owns(MediaFormat.Dvd).ShouldBeFalse();
        movie.Owns(MediaFormat.BluRay).ShouldBeTrue();
    }

    [Fact]
    public void An_invalid_update_changes_nothing()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd)], Jan2019);

        Should.Throw<DomainValidationException>(() => movie.Update(Heat with { Title = "" }, [], Mar2025));

        movie.Title.ShouldBe("Heat");
        movie.Owns(MediaFormat.Dvd).ShouldBeTrue();
        movie.Version.ShouldBe(1);
    }

    [Fact]
    public void Removing_every_format_is_rejected()
    {
        var movie = Movie.Create(Heat, [new(MediaFormat.Dvd)], Jan2019);

        var ex = Should.Throw<DomainValidationException>(() => movie.Update(Heat, [], Mar2025));

        ex.Errors.ShouldHaveSingleError(MovieFields.Formats);
    }
}
