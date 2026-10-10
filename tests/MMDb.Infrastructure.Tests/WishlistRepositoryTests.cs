using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Wishlist;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

public sealed class WishlistRepositoryTests
{
    private static readonly DateTimeOffset Sep2026 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WishlistMovie Wish(Domain.Movies.MovieDetails film, MediaFormat format = MediaFormat.Uhd4K) =>
        WishlistMovie.Create(TestMovies.Wanted(film, format), Sep2026);

    private static async Task SaveAsync(TestDatabase database, params WishlistMovie[] items)
    {
        await using var db = database.NewContext();
        var wishlist = new EfWishlistRepository(db);
        foreach (var item in items)
        {
            wishlist.Add(item);
        }

        await wishlist.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_saved_wish_round_trips_every_field()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = Wish(TestMovies.HeatOnTmdb, MediaFormat.BluRay);
        saved.UseCover("cover.jpg");
        await SaveAsync(database, saved);

        await using var db = database.NewContext();
        var loaded = (await new EfWishlistRepository(db).FindMovieAsync(saved.Id, Ct)).ShouldNotBeNull();

        loaded.ShouldSatisfyAllConditions(
            l => l.Title.ShouldBe("Heat"),
            l => l.Year.ShouldBe(1995),
            l => l.Directors.ShouldBe(["Michael Mann"]),
            l => l.TmdbId.ShouldBe(949),
            l => l.TrailerYouTubeKey.ShouldBe("0xbBLJ1WGwQ"),
            l => l.PosterPath.ShouldBe("/heat.jpg"),
            l => l.Format.ShouldBe(MediaFormat.BluRay),
            l => l.CoverImage.ShouldBe("cover.jpg"),
            l => l.CreatedAt.ShouldBe(Sep2026),
            l => l.Version.ShouldBe(1));
    }

    [Fact]
    public async Task Wishes_for_films_are_found_by_tmdb_id_along_with_those_that_have_none()
    {
        using var database = await TestDatabase.MigratedAsync();
        await SaveAsync(database, Wish(TestMovies.HeatOnTmdb), Wish(TestMovies.Heat1986OnTmdb), Wish(TestMovies.Titled("Alien", 1979)));

        await using var db = database.NewContext();
        var found = await new EfWishlistRepository(db).FindMoviesForAsync([949, 42113], Ct);

        found.Select(w => w.Title).ShouldBe(["Heat", "Alien"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_saved_comic_wish_round_trips_every_field()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = WishlistComic.Create(
            new WantedComic
            {
                Series = "Daredevil",
                SeriesYear = 1964,
                Number = "159",
                Publisher = "Marvel",
                MetronId = 9001,
                MetronSeriesId = 77,
                CoverUrl = "https://static.metron.cloud/media/issue/dd-159.jpg",
                FromMissing = true,
            },
            Sep2026);
        var book = WishlistComic.Create(
            new WantedComic { Format = Domain.Comics.ComicFormat.Hardcover, Title = "Saga: Book One", Isbn = "9781632150783" }, Sep2026);
        saved.UseCover("dd.jpg");
        await using (var db = database.NewContext())
        {
            var wishlist = new EfWishlistRepository(db);
            wishlist.Add(saved);
            wishlist.Add(book);
            await wishlist.SaveChangesAsync(Ct);
        }

        await using var read = database.NewContext();
        var repository = new EfWishlistRepository(read);
        var loaded = (await repository.FindComicAsync(saved.Id, Ct)).ShouldNotBeNull();
        var loadedBook = (await repository.ListComicsAsync(Ct)).Single(c => c.Id == book.Id);

        loaded.ShouldSatisfyAllConditions(
            l => l.DisplayName.ShouldBe("Daredevil #159"),
            l => l.Format.ShouldBe(Domain.Comics.ComicFormat.SingleIssue),
            l => l.Series.ShouldBe("Daredevil"),
            l => l.SeriesYear.ShouldBe(1964),
            l => l.Number.ShouldBe("159"),
            l => l.Publisher.ShouldBe("Marvel"),
            l => l.MetronSeriesId.ShouldBe(77),
            l => l.MetronId.ShouldBe(9001),
            l => l.CoverUrl.ShouldBe("https://static.metron.cloud/media/issue/dd-159.jpg"),
            l => l.CoverImage.ShouldBe("dd.jpg"),
            l => l.FromMissing.ShouldBeTrue(),
            l => l.CreatedAt.ShouldBe(Sep2026));
        loadedBook.ShouldSatisfyAllConditions(
            l => l.Format.ShouldBe(Domain.Comics.ComicFormat.Hardcover),
            l => l.Title.ShouldBe("Saga: Book One"),
            l => l.Isbn.ShouldBe("9781632150783"),
            l => l.Number.ShouldBeNull());
        (await repository.LoadComicsAsync(Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Removing_a_wish_someone_else_removed_first_is_a_conflict()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = Wish(TestMovies.HeatOnTmdb);
        await SaveAsync(database, saved);

        await using var mine = database.NewContext();
        await using var theirs = database.NewContext();
        var myWishlist = new EfWishlistRepository(mine);
        var theirWishlist = new EfWishlistRepository(theirs);
        myWishlist.Remove((await myWishlist.FindMovieAsync(saved.Id, Ct))!);
        theirWishlist.Remove((await theirWishlist.FindMovieAsync(saved.Id, Ct))!);
        await theirWishlist.SaveChangesAsync(Ct);

        await Should.ThrowAsync<ConcurrencyConflictException>(() => myWishlist.SaveChangesAsync(Ct));
    }
}
