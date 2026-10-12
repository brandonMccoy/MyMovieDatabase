using MMDb.Application.Types;
using MMDb.Domain;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>The types of collection Shelf shows: chosen in Settings, remembered, and never a reason to delete anything.</summary>
public sealed class TypeChoiceTests : IAsyncLifetime
{
    private TestShelf _shelf = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TypeChoice Choice => _shelf.Types;

    public async ValueTask InitializeAsync() => _shelf = await TestShelf.CreateAsync();

    public ValueTask DisposeAsync() => _shelf.DisposeAsync();

    [Fact]
    public async Task Every_type_is_in_use_until_one_is_turned_off()
    {
        (await Choice.ReadAsync(Ct)).ShouldBe(new TypesInUse(Movies: true, Comics: true));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_type_turned_off_stays_off_until_it_is_turned_on_again(bool movies, bool comics)
    {
        (await Choice.SaveAsync(new(movies, comics), Ct)).ShouldBe(new TypesInUse(movies, comics));
        (await Choice.ReadAsync(Ct)).ShouldBe(new TypesInUse(movies, comics));

        await Choice.SaveAsync(TypesInUse.All, Ct);

        (await Choice.ReadAsync(Ct)).ShouldBe(TypesInUse.All);
    }

    [Fact]
    public async Task At_least_one_type_stays_in_use()
    {
        await Choice.SaveAsync(new(Movies: false, Comics: true), Ct);

        (await Choice.SaveAsync(new(Movies: false, Comics: false), Ct)).ShouldBe(new TypesInUse(Movies: false, Comics: true));
        (await Choice.ReadAsync(Ct)).ShouldBe(new TypesInUse(Movies: false, Comics: true));
    }

    [Fact]
    public async Task Settings_that_say_no_type_at_all_are_read_as_every_type()
    {
        await _shelf.Settings.SetAsync("types.movies", "off", Ct);
        await _shelf.Settings.SetAsync("types.comics", "off", Ct);

        (await Choice.ReadAsync(Ct)).ShouldBe(TypesInUse.All);
    }

    [Fact]
    public async Task Turning_a_type_off_deletes_nothing_that_was_saved_for_it()
    {
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.AddComicAsync(TestComics.Saga7);
        await _shelf.WantAsync(TestMovies.Heat1986OnTmdb, MediaFormat.BluRay);

        await Choice.SaveAsync(new(Movies: false, Comics: true), Ct);

        (await _shelf.ListAsync()).Movies.ShouldHaveSingleItem().Title.ShouldBe("Heat");
        (await _shelf.WishlistAsync()).Movies.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Stats_do_not_count_a_type_that_is_not_in_use()
    {
        await _shelf.AddAsync(TestMovies.Heat, MediaFormat.Dvd);
        await _shelf.AddComicAsync(TestComics.Saga7);
        await _shelf.WantAsync(TestMovies.Heat1986OnTmdb, MediaFormat.BluRay);
        await _shelf.Wishlist((wishlist, ct) => wishlist.WantComicAsync(new MMDb.Domain.Wishlist.WantedComic { Series = "Saga", Number = "8" }, ct));

        var movies = await _shelf.StatsAsync(new(Movies: true, Comics: false));
        var comics = await _shelf.StatsAsync(new(Movies: false, Comics: true));

        movies.ShouldSatisfyAllConditions(
            s => s.FilmCount.ShouldBe(1),
            s => s.WishlistMovieCount.ShouldBe(1),
            s => s.IssueCount.ShouldBe(0),
            s => s.SeriesCount.ShouldBe(0),
            s => s.WishlistComicCount.ShouldBe(0),
            s => s.ComicsAddedByMonth.ShouldAllBe(m => m.Count == 0),
            s => s.Publishers.ShouldBeEmpty());
        comics.ShouldSatisfyAllConditions(
            s => s.IssueCount.ShouldBe(1),
            s => s.WishlistComicCount.ShouldBe(1),
            s => s.FilmCount.ShouldBe(0),
            s => s.DiscCount.ShouldBe(0),
            s => s.WishlistMovieCount.ShouldBe(0),
            s => s.MoviesAddedByMonth.ShouldAllBe(m => m.Count == 0),
            s => s.TopDirectors.ShouldBeEmpty(),
            s => s.TopActors.ShouldBeEmpty());
    }
}
