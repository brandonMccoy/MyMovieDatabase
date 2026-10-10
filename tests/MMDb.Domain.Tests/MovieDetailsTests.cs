using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class MovieDetailsTests
{
    [Fact]
    public void A_title_alone_is_valid()
    {
        new MovieDetails { Title = "Heat" }.Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string title)
    {
        new MovieDetails { Title = title }.Validate().ShouldHaveSingleError(MovieFields.Title);
    }

    [Fact]
    public void Title_has_a_maximum_length()
    {
        new MovieDetails { Title = new string('x', MovieDetails.MaxTitleLength) }.Validate().ShouldBeEmpty();
        new MovieDetails { Title = new string('x', MovieDetails.MaxTitleLength + 1) }.Validate().ShouldHaveSingleError(MovieFields.Title);
    }

    [Theory]
    [InlineData(1888)]
    [InlineData(1995)]
    [InlineData(2100)]
    public void Years_from_the_first_film_onward_are_valid(int year)
    {
        new MovieDetails { Title = "Heat", Year = year }.Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1887)]
    [InlineData(2101)]
    [InlineData(0)]
    [InlineData(-1995)]
    public void Years_out_of_range_are_rejected(int year)
    {
        new MovieDetails { Title = "Heat", Year = year }.Validate().ShouldHaveSingleError(MovieFields.Year);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void Runtime_must_be_a_plausible_number_of_minutes(int minutes)
    {
        new MovieDetails { Title = "Heat", RuntimeMinutes = minutes }.Validate().ShouldHaveSingleError(MovieFields.RuntimeMinutes);
    }

    [Fact]
    public void Rating_has_a_maximum_length()
    {
        new MovieDetails { Title = "Heat", Rating = "PG-13" }.Validate().ShouldBeEmpty();
        new MovieDetails { Title = "Heat", Rating = new string('R', 17) }.Validate().ShouldHaveSingleError(MovieFields.Rating);
    }

    [Fact]
    public void A_cast_member_with_a_role_but_no_name_is_rejected()
    {
        var details = new MovieDetails { Title = "Heat", Cast = [new("", "Vincent Hanna")] };

        details.Validate().ShouldHaveSingleError(MovieFields.Cast);
    }

    [Fact]
    public void Normalizing_trims_text_and_turns_blanks_into_nulls()
    {
        var details = new MovieDetails { Title = "  Heat ", Rating = " ", Overview = "\n", TrailerYouTubeKey = "" }.Normalized();

        details.Title.ShouldBe("Heat");
        details.Rating.ShouldBeNull();
        details.Overview.ShouldBeNull();
        details.TrailerYouTubeKey.ShouldBeNull();
    }

    [Fact]
    public void Normalizing_drops_blank_and_repeated_directors_and_genres()
    {
        var details = new MovieDetails
        {
            Title = "No Country for Old Men",
            Directors = [" Joel Coen", "Ethan Coen", "", "joel coen"],
            Genres = ["Crime", " ", "Thriller", "crime "],
        }.Normalized();

        details.Directors.ShouldBe(["Joel Coen", "Ethan Coen"]);
        details.Genres.ShouldBe(["Crime", "Thriller"]);
    }

    [Fact]
    public void A_crew_credit_needs_a_name_and_a_job()
    {
        new MovieDetails { Title = "Heat", Crew = [new("", "Editor", "Editing")] }.Validate().ShouldHaveSingleError(MovieFields.Crew);
        new MovieDetails { Title = "Heat", Crew = [new("Pasquale Buba", " ", "Editing")] }.Validate().ShouldHaveSingleError(MovieFields.Crew);
        new MovieDetails { Title = "Heat", Crew = [new("Pasquale Buba", "Editor")] }.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Normalizing_trims_the_crew_and_drops_blank_and_repeated_credits()
    {
        var details = new MovieDetails
        {
            Title = "Heat",
            Crew = [new(" Pasquale Buba ", " Editor ", " Editing "), new("", "", "Crew"), new("Pasquale Buba", "Editor", "Editing"), new("Dov Hoenig", "Editor", " ")],
        }.Normalized();

        details.Crew.ShouldBe([new("Pasquale Buba", "Editor", "Editing"), new("Dov Hoenig", "Editor")]);
    }

    [Fact]
    public void Normalizing_puts_the_crew_in_credit_order()
    {
        var details = new MovieDetails
        {
            Title = "Heat",
            Crew =
            [
                new("Somebody Else", "Thanks"),
                new("Kathy Shorkey", "Makeup Artist", "Costume & Make-Up"),
                new("Stunt Person", "Stunts", "Stunts"),
                new("Gusmano Cesaretti", "Associate Producer", "Production"),
                new("Arnon Milchan", "Executive Producer", "Production"),
                new("Dante Spinotti", "Director of Photography", "Camera"),
                new("Art Linson", "Producer", "Production"),
                new("Michael Mann", "Producer", "Production"),
                new("Michael Mann", "Screenplay", "Writing"),
                new("Animal Wrangler", "Animal Wrangler", "Animals"),
            ],
        }.Normalized();

        details.Crew.Select(c => $"{c.Department}: {c.Name}, {c.Job}").ShouldBe(
        [
            "Writing: Michael Mann, Screenplay",
            "Production: Art Linson, Producer",
            "Production: Michael Mann, Producer",
            "Production: Arnon Milchan, Executive Producer",
            "Production: Gusmano Cesaretti, Associate Producer",
            "Camera: Dante Spinotti, Director of Photography",
            "Costume & Make-Up: Kathy Shorkey, Makeup Artist",
            "Animals: Animal Wrangler, Animal Wrangler",
            "Stunts: Stunt Person, Stunts",
            ": Somebody Else, Thanks",
        ]);
    }

    [Fact]
    public void Headline_names_are_listed_once_each_in_the_order_of_their_jobs()
    {
        IReadOnlyList<CrewMember> crew =
        [
            new("Eric Roth", "Story", "Writing"),
            new("Michael Mann", "Screenplay", "Writing"),
            new("Michael Mann", "Story", "Writing"),
            new("Art Linson", "Producer", "Production"),
        ];

        CrewCredits.NamesFor(crew, CrewCredits.WritingJobs).ShouldBe(["Michael Mann", "Eric Roth"]);
        CrewCredits.NamesFor(crew, CrewCredits.Editor).ShouldBeEmpty();
        crew.Count(CrewCredits.IsHeadline).ShouldBe(4);
        CrewCredits.IsHeadline(new("Neil Spisak", "Production Design", "Art")).ShouldBeFalse();
    }

    [Fact]
    public void Normalizing_drops_empty_cast_rows_and_trims_names_and_roles()
    {
        var details = new MovieDetails
        {
            Title = "Heat",
            Cast = [new(" Al Pacino ", " Vincent Hanna "), new("", " "), new("Robert De Niro")],
        }.Normalized();

        details.Cast.ShouldBe([new("Al Pacino", "Vincent Hanna"), new("Robert De Niro")]);
    }

    [Theory]
    [InlineData("/rrBuGu0Pjq7Y2BWSI6teGfZzviY.jpg")]
    [InlineData("/a-b_C9.png")]
    public void A_tmdb_poster_path_is_accepted(string path)
    {
        new MovieDetails { Title = "Heat", PosterPath = path }.Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("rrBuGu0Pjq7Y2BWSI6teGfZzviY.jpg")]
    [InlineData("/../../secrets.jpg")]
    [InlineData("/a/b.jpg")]
    [InlineData("/poster.exe")]
    [InlineData("https://example.com/poster.jpg")]
    public void Anything_else_as_a_poster_path_is_rejected(string path)
    {
        new MovieDetails { Title = "Heat", PosterPath = path }.Validate().ShouldHaveSingleError(MovieFields.PosterPath);
    }

    [Fact]
    public void A_blank_poster_path_means_no_poster()
    {
        new MovieDetails { Title = "Heat", PosterPath = " " }.Normalized().PosterPath.ShouldBeNull();
    }
}
