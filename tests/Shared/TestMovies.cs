using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

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
    public static MovieDetails HeatOnTmdb => Heat with { TmdbId = 949, PosterPath = "/heat.jpg", TrailerYouTubeKey = "0xbBLJ1WGwQ", Crew = HeatCrew };

    /// <summary>Some of Heat's crew, in credit order.</summary>
    public static IReadOnlyList<CrewMember> HeatCrew =>
    [
        new("Michael Waxman", "First Assistant Director", "Directing"),
        new("Michael Mann", "Screenplay", "Writing"),
        new("Art Linson", "Producer", "Production"),
        new("Michael Mann", "Producer", "Production"),
        new("Arnon Milchan", "Executive Producer", "Production"),
        new("Gusmano Cesaretti", "Associate Producer", "Production"),
        new("Dante Spinotti", "Director of Photography", "Camera"),
        new("Pasquale Buba", "Editor", "Editing"),
        new("Elliot Goldenthal", "Original Music Composer", "Sound"),
        new("Neil Spisak", "Production Design", "Art"),
        new("Neil Spisak", "Set Designer", "Art"),
    ];

    /// <summary>The other "Heat"s TMDB knows.</summary>
    public static MovieDetails Heat1986OnTmdb => new() { Title = "Heat", Year = 1986, TmdbId = 31939, Directors = ["Dick Richards"], PosterPath = "/heat86.jpg" };

    public static MovieDetails HeatAndDustOnTmdb => new() { Title = "Heat and Dust", Year = 1983, TmdbId = 42113, Directors = ["James Ivory"] };

    public static MovieDetails Titled(string title, int? year = null) => new() { Title = title, Year = year };

    /// <summary>The film as it is put on the wishlist.</summary>
    public static WantedMovie Wanted(MovieDetails film, MediaFormat format) => new()
    {
        Title = film.Title,
        Year = film.Year,
        Directors = film.Directors,
        TmdbId = film.TmdbId,
        TrailerYouTubeKey = film.TrailerYouTubeKey,
        PosterPath = film.PosterPath,
        Format = format,
    };

    public static FormatDetails[] On(params MediaFormat[] formats) => [.. formats.Select(f => new FormatDetails(f))];
}
