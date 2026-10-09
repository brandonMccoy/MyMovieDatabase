using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Testing;

namespace MMDb.Application.Tests;

public sealed class MovieListTests
{
    private static MovieDetails Film(string title, int? year = null, string[]? directors = null, string[]? cast = null, string[]? genres = null) => new()
    {
        Title = title,
        Year = year,
        Directors = directors ?? [],
        Cast = [.. (cast ?? []).Select(name => new CastMember(name))],
        Genres = genres ?? [],
    };

    private static async Task<string[]> TitlesAsync(TestShelf shelf, MovieQuery query) =>
        [.. (await shelf.ListAsync(query)).Movies.Select(m => m.Title)];

    [Fact]
    public async Task An_empty_collection_lists_nothing()
    {
        await using var shelf = await TestShelf.CreateAsync();

        var list = await shelf.ListAsync();

        list.Movies.ShouldBeEmpty();
        list.Facets.FilmCount.ShouldBe(0);
        list.Facets.DiscCount.ShouldBe(0);
        list.Facets.Genres.ShouldBeEmpty();
        list.Facets.Decades.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("heat")]
    [InlineData("HEAT")]
    [InlineData("ea")]
    public async Task Search_finds_titles_ignoring_case(string search)
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien"), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Search = search })).ShouldBe(["Heat"]);
    }

    [Fact]
    public async Task Search_ignores_accents()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Amélie", 2001, directors: ["Jean-Pierre Jeunet"]), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Search = "amelie" })).ShouldBe(["Amélie"]);
    }

    [Fact]
    public async Task Search_finds_directors_and_cast()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", directors: ["Michael Mann"], cast: ["Al Pacino"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Collateral", directors: ["Michael Mann"], cast: ["Tom Cruise"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("The Godfather", directors: ["Francis Ford Coppola"], cast: ["Al Pacino"]), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Search = "mann" })).ShouldBe(["Heat", "Collateral"], ignoreOrder: true);
        (await TitlesAsync(shelf, new() { Search = "pacino" })).ShouldBe(["Heat", "The Godfather"], ignoreOrder: true);
    }

    [Fact]
    public async Task Every_search_word_must_match_something()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", 1995, directors: ["Michael Mann"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Collateral", 2004, directors: ["Michael Mann"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Casino", 1995, directors: ["Martin Scorsese"]), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Search = "  mann   1995 " })).ShouldBe(["Heat"]);
    }

    [Fact]
    public async Task Filtering_by_format_shows_films_owned_on_it()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat"), MediaFormat.Uhd4K, MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien"), MediaFormat.BluRay);

        (await TitlesAsync(shelf, new() { Format = MediaFormat.Dvd })).ShouldBe(["Heat"]);
        (await TitlesAsync(shelf, new() { Format = MediaFormat.Digital })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Filtering_by_genre_ignores_case()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", genres: ["Crime", "Drama"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien", genres: ["Horror"]), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Genre = "crime" })).ShouldBe(["Heat"]);
    }

    [Fact]
    public async Task Filtering_by_decade_includes_every_year_in_it()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Alien", 1979), MediaFormat.Dvd);
        await shelf.AddAsync(Film("The Thing", 1982), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Die Hard", 1988), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Heat", 1995), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Untitled"), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Decade = 1980 })).ShouldBe(["The Thing", "Die Hard"], ignoreOrder: true);
    }

    [Fact]
    public async Task Facets_count_the_whole_collection_and_options()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", 1995, genres: ["Crime", "Drama"]), MediaFormat.Uhd4K, MediaFormat.Dvd, MediaFormat.Digital);
        await shelf.AddAsync(Film("Alien", 1979, genres: ["Horror", "crime"]), MediaFormat.BluRay);
        await shelf.AddAsync(Film("Arrival", 2016, genres: ["Drama"]), MediaFormat.Digital);

        var facets = (await shelf.ListAsync(new() { Search = "heat" })).Facets;

        facets.FilmCount.ShouldBe(3);
        facets.DiscCount.ShouldBe(3); // 4K + DVD + Blu-ray; digital copies are not discs
        facets.Genres.ShouldBe(["Crime", "Drama", "Horror"]);
        facets.Decades.ShouldBe([2010, 1990, 1970]);
    }

    [Fact]
    public async Task Format_counts_follow_the_search_but_not_the_format_filter()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", directors: ["Michael Mann"]), MediaFormat.Uhd4K, MediaFormat.Dvd);
        await shelf.AddAsync(Film("Collateral", directors: ["Michael Mann"]), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien"), MediaFormat.Dvd, MediaFormat.BluRay);

        var list = await shelf.ListAsync(new() { Search = "mann", Format = MediaFormat.Uhd4K });

        list.Movies.Select(m => m.Title).ShouldBe(["Heat"]);
        list.Facets.MatchingCount.ShouldBe(2);
        list.Facets.FormatCounts.ShouldBe(new Dictionary<MediaFormat, int>
        {
            [MediaFormat.Uhd4K] = 1,
            [MediaFormat.BluRay] = 0,
            [MediaFormat.Dvd] = 2,
            [MediaFormat.Digital] = 0,
        });
    }

    [Fact]
    public async Task Recently_added_puts_the_newest_format_first()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var heat = await shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(1));
        await shelf.AddAsync(Film("Alien"), MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(1));
        await shelf.AddAsync(Film("Arrival"), MediaFormat.Digital);
        shelf.Clock.Advance(TimeSpan.FromDays(1));

        (await TitlesAsync(shelf, new() { Sort = MovieSort.RecentlyAdded })).ShouldBe(["Arrival", "Alien", "Heat"]);

        await shelf.Movies((catalog, ct) => catalog.UpdateAsync(heat.Id, heat.Version, Film("Heat"), TestMovies.On(MediaFormat.Dvd, MediaFormat.Uhd4K), ct));

        (await TitlesAsync(shelf, new() { Sort = MovieSort.RecentlyAdded })).ShouldBe(["Heat", "Arrival", "Alien"]);
    }

    [Fact]
    public async Task Editing_details_alone_does_not_count_as_recently_added()
    {
        await using var shelf = await TestShelf.CreateAsync();
        var heat = await shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(1));
        await shelf.AddAsync(Film("Alien"), MediaFormat.Dvd);
        shelf.Clock.Advance(TimeSpan.FromDays(1));

        await shelf.Movies((catalog, ct) => catalog.UpdateAsync(heat.Id, heat.Version, Film("Heat", 1995), TestMovies.On(MediaFormat.Dvd), ct));

        (await TitlesAsync(shelf, new() { Sort = MovieSort.RecentlyAdded })).ShouldBe(["Alien", "Heat"]);
    }

    [Fact]
    public async Task Title_order_ignores_a_leading_article()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("The Godfather"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("A Bug's Life"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Theory of Everything"), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Sort = MovieSort.Title }))
            .ShouldBe(["Alien", "A Bug's Life", "The Godfather", "Heat", "Theory of Everything"]);
    }

    [Fact]
    public async Task Year_order_is_newest_first_with_undated_films_last()
    {
        await using var shelf = await TestShelf.CreateAsync();
        await shelf.AddAsync(Film("Heat", 1995), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Untitled"), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Arrival", 2016), MediaFormat.Dvd);
        await shelf.AddAsync(Film("Alien", 1979), MediaFormat.Dvd);

        (await TitlesAsync(shelf, new() { Sort = MovieSort.Year })).ShouldBe(["Arrival", "Heat", "Alien", "Untitled"]);
    }
}
