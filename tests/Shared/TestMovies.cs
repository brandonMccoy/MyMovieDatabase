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

    public static MovieDetails Titled(string title, int? year = null) => new() { Title = title, Year = year };

    public static FormatDetails[] On(params MediaFormat[] formats) => [.. formats.Select(f => new FormatDetails(f))];
}
