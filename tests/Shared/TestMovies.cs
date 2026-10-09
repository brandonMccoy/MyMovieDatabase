using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Testing;

/// <summary>Sample input with sensible defaults; tweak per test with <c>with { … }</c>.</summary>
internal static class TestMovies
{
    public static MovieDetails Heat => new()
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

    /// <summary>Heat as TMDB has it.</summary>
    public static MovieDetails HeatOnTmdb => Heat with { TmdbId = 949, PosterPath = "/heat.jpg", TrailerYouTubeKey = "0xbBLJ1WGwQ" };

    /// <summary>The other "Heat"s TMDB knows.</summary>
    public static MovieDetails Heat1986OnTmdb => new() { Title = "Heat", Year = 1986, TmdbId = 31939, Directors = ["Dick Richards"], PosterPath = "/heat86.jpg" };

    public static MovieDetails HeatAndDustOnTmdb => new() { Title = "Heat and Dust", Year = 1983, TmdbId = 42113, Directors = ["James Ivory"] };

    public static MovieDetails Titled(string title, int? year = null) => new() { Title = title, Year = year };

    public static FormatDetails[] On(params MediaFormat[] formats) => [.. formats.Select(f => new FormatDetails(f))];
}
