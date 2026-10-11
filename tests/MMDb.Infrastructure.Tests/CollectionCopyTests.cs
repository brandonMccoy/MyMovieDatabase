using System.Text.Json;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>Rows read from one database (the PC's) and written into another (a phone's).</summary>
public sealed class CollectionCopyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Sep2026 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private TestDatabase _pc = null!;
    private TestDatabase _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CollectionCopy Pc => new(_pc.Options);

    private CollectionCopy Phone => new(_phone.Options);

    public async ValueTask InitializeAsync()
    {
        _pc = await TestDatabase.MigratedAsync();
        _phone = await TestDatabase.MigratedAsync();
    }

    public ValueTask DisposeAsync()
    {
        _pc.Dispose();
        _phone.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Movie Heat(params MediaFormat[] formats)
    {
        var film = Movie.Create(TestMovies.HeatOnTmdb, [new FormatDetails(formats[0]) { Upc = "786936894424" }, .. TestMovies.On(formats[1..])], Sep2026);
        film.UseCover($"{film.Id}-heat.jpg");
        return film;
    }

    private async Task<Movie> SaveOnPcAsync(Movie film)
    {
        await using var db = _pc.NewContext();
        var movies = new EfMovieRepository(db);
        movies.Add(film);
        await movies.SaveChangesAsync(Ct);
        return film;
    }

    /// <summary>Brings the phone's table in line with the PC's, the way a sync does.</summary>
    private async Task CopyAsync(string table)
    {
        var pc = (await Pc.ListAsync(Ct)).Single(t => t.Name == table).Stamps;
        var phone = (await Phone.ListAsync(Ct)).Single(t => t.Name == table).Stamps;
        await Phone.RemoveAsync(table, [.. phone.Keys.Except(pc.Keys)], Ct);
        var changed = pc.Where(row => phone.GetValueOrDefault(row.Key) != row.Value).Select(row => row.Key).ToList();
        await Phone.WriteAsync(table, await Pc.ReadAsync(table, changed, Ct), Ct);
    }

    [Fact]
    public async Task Both_databases_name_the_same_schema_the_newest_migration()
    {
        var schema = await Pc.SchemaAsync(Ct);

        schema.ShouldMatch(@"^\d{14}_\w+$");
        (await Phone.SchemaAsync(Ct)).ShouldBe(schema);
    }

    [Fact]
    public async Task A_film_arrives_on_the_phone_whole_with_its_formats_and_credits()
    {
        var saved = await SaveOnPcAsync(Heat(MediaFormat.Uhd4K, MediaFormat.Dvd));

        await CopyAsync("Movies");

        await using var db = _phone.NewContext();
        var movies = new EfMovieRepository(db);
        var copy = (await movies.FindAsync(saved.Id, Ct)).ShouldNotBeNull();
        Json(copy.Details).ShouldBe(Json(saved.Details));
        copy.ShouldSatisfyAllConditions(
            c => c.Version.ShouldBe(saved.Version),
            c => c.CreatedAt.ShouldBe(Sep2026),
            c => c.UpdatedAt.ShouldBe(Sep2026),
            c => c.CoverImage.ShouldBe(saved.CoverImage),
            c => c.Formats.Select(f => f.Format).ShouldBe([MediaFormat.Uhd4K, MediaFormat.Dvd]),
            c => c.Formats[0].Upc.ShouldBe(Upc.Parse("786936894424")),
            c => c.Formats[0].AddedAt.ShouldBe(Sep2026));

        // Found by its barcode and by who is in it, as on the PC.
        (await movies.FindByUpcAsync(Upc.Parse("786936894424"), Ct)).ShouldNotBeNull().Id.ShouldBe(saved.Id);
        (await movies.FindCreditingAsync("al pacino", Ct)).ShouldBe([saved.Id]);
    }

    [Fact]
    public async Task Only_rows_whose_stamp_changed_need_fetching_and_a_cover_alone_changes_the_stamp()
    {
        var heat = await SaveOnPcAsync(Movie.Create(TestMovies.HeatOnTmdb, TestMovies.On(MediaFormat.BluRay), Sep2026));
        var other = await SaveOnPcAsync(Movie.Create(TestMovies.Heat1986OnTmdb, TestMovies.On(MediaFormat.Dvd), Sep2026));
        await CopyAsync("Movies");
        var before = (await Pc.ListAsync(Ct)).Single(t => t.Name == "Movies").Stamps;
        (await Phone.ListAsync(Ct)).Single(t => t.Name == "Movies").Stamps.ShouldBe(before, ignoreOrder: true);

        // The cover arrives after the save, without a new version.
        await using (var db = _pc.NewContext())
        {
            var movies = new EfMovieRepository(db);
            (await movies.FindAsync(heat.Id, Ct)).ShouldNotBeNull().UseCover("late.jpg");
            await movies.SaveChangesAsync(Ct);
        }

        var after = (await Pc.ListAsync(Ct)).Single(t => t.Name == "Movies").Stamps;

        after[Key(other.Id)].ShouldBe(before[Key(other.Id)]);
        after[Key(heat.Id)].ShouldNotBe(before[Key(heat.Id)]);
    }

    [Fact]
    public async Task A_film_changed_on_the_pc_replaces_the_phones_and_a_format_it_lost_goes()
    {
        var saved = await SaveOnPcAsync(Heat(MediaFormat.Uhd4K, MediaFormat.Dvd));
        await CopyAsync("Movies");
        await using (var db = _pc.NewContext())
        {
            var movies = new EfMovieRepository(db);
            var film = (await movies.FindAsync(saved.Id, Ct)).ShouldNotBeNull();
            film.Update(film.Details with { Title = "Heat (Director's Definitive Edition)" }, TestMovies.On(MediaFormat.Uhd4K, MediaFormat.Digital), Sep2026.AddDays(1));
            await movies.SaveChangesAsync(Ct);
        }

        await CopyAsync("Movies");

        await using var phone = _phone.NewContext();
        var copy = (await new EfMovieRepository(phone).FindAsync(saved.Id, Ct)).ShouldNotBeNull();
        copy.Title.ShouldBe("Heat (Director's Definitive Edition)");
        copy.Version.ShouldBe(2);
        copy.Formats.Select(f => f.Format).ShouldBe([MediaFormat.Uhd4K, MediaFormat.Digital]);
    }

    [Fact]
    public async Task A_film_removed_on_the_pc_goes_from_the_phone_with_its_formats()
    {
        var gone = await SaveOnPcAsync(Heat(MediaFormat.Uhd4K, MediaFormat.Dvd));
        var kept = await SaveOnPcAsync(Movie.Create(TestMovies.Heat1986OnTmdb, TestMovies.On(MediaFormat.Dvd), Sep2026));
        await CopyAsync("Movies");
        await using (var db = _pc.NewContext())
        {
            var movies = new EfMovieRepository(db);
            movies.Remove((await movies.FindAsync(gone.Id, Ct)).ShouldNotBeNull());
            await movies.SaveChangesAsync(Ct);
        }

        await CopyAsync("Movies");

        await using var phone = _phone.NewContext();
        (await new EfMovieRepository(phone).ListAsync(Ct)).ShouldHaveSingleItem().Id.ShouldBe(kept.Id);
        (await new EfMovieRepository(phone).FindByUpcAsync(Upc.Parse("786936894424"), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Comics_their_series_and_the_wishlist_are_copied_too()
    {
        var comic = Comic.Create(TestComics.Saga7, Sep2026);
        var series = ComicSeries.Create(4242, Sep2026, new("Saga", 2012, "Image", ["1", "2", "7"]));
        var wantedFilm = WishlistMovie.Create(TestMovies.Wanted(TestMovies.HeatOnTmdb, MediaFormat.Uhd4K), Sep2026);
        var wantedComic = WishlistComic.Create(new() { Format = ComicFormat.SingleIssue, Series = "Saga", SeriesYear = 2012, Number = "8" }, Sep2026);
        await using (var db = _pc.NewContext())
        {
            var comics = new EfComicRepository(db);
            comics.Add(comic);
            comics.AddSeries(series);
            await comics.SaveChangesAsync(Ct);
            var wishlist = new EfWishlistRepository(db);
            wishlist.Add(wantedFilm);
            wishlist.Add(wantedComic);
            await wishlist.SaveChangesAsync(Ct);
        }

        foreach (var table in CollectionCopy.Tables)
        {
            await CopyAsync(table);
        }

        await using var phone = _phone.NewContext();
        (await new EfComicRepository(phone).FindAsync(comic.Id, Ct)).ShouldNotBeNull().Details.ShouldSatisfyAllConditions(d => Json(d).ShouldBe(Json(comic.Details)));
        (await new EfComicRepository(phone).FindSeriesAsync(4242, Ct)).ShouldNotBeNull().ShouldSatisfyAllConditions(
            s => s.Name.ShouldBe("Saga"),
            s => s.IssueNumbers.ShouldBe(["1", "2", "7"]),
            s => s.FetchedAt.ShouldBe(Sep2026));
        (await new EfWishlistRepository(phone).FindMovieAsync(wantedFilm.Id, Ct)).ShouldNotBeNull().Format.ShouldBe(MediaFormat.Uhd4K);
        (await new EfWishlistRepository(phone).FindComicAsync(wantedComic.Id, Ct)).ShouldNotBeNull().Number.ShouldBe("8");
    }

    [Fact]
    public async Task The_covers_to_fetch_are_those_the_copied_rows_show_each_once()
    {
        var heat = await SaveOnPcAsync(Heat(MediaFormat.Uhd4K));
        await SaveOnPcAsync(Movie.Create(TestMovies.Heat1986OnTmdb, TestMovies.On(MediaFormat.Dvd), Sep2026));
        var wanted = WishlistMovie.Create(TestMovies.Wanted(TestMovies.HeatAndDustOnTmdb, MediaFormat.Dvd), Sep2026);
        wanted.UseCover("wanted.jpg");
        await using (var db = _pc.NewContext())
        {
            var wishlist = new EfWishlistRepository(db);
            wishlist.Add(wanted);
            await wishlist.SaveChangesAsync(Ct);
        }

        (await Pc.CoverNamesAsync(Ct)).ShouldBe([heat.CoverImage!, "wanted.jpg"], ignoreOrder: true);
    }

    [Fact]
    public async Task Rows_from_a_database_with_other_columns_are_turned_down_and_nothing_is_written()
    {
        var saved = await SaveOnPcAsync(Heat(MediaFormat.Uhd4K));
        var kept = await SaveOnPcAsync(Movie.Create(TestMovies.Heat1986OnTmdb, TestMovies.On(MediaFormat.Dvd), Sep2026));
        var rows = await Pc.ReadAsync("Movies", [Key(kept.Id), Key(saved.Id)], Ct);
        var newer = rows[1] with { Values = new Dictionary<string, object?>(rows[1].Values) { ["Tagline"] = "A Los Angeles crime saga" } };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => Phone.WriteAsync("Movies", [rows[0], newer], Ct));

        refused.Message.ShouldContain("Tagline");
        (await Phone.ListAsync(Ct)).Single(t => t.Name == "Movies").Stamps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_collections_tables_can_be_copied()
    {
        await Should.ThrowAsync<ArgumentException>(() => Pc.ReadAsync("PairedDevices", ["x"], Ct));
        await Should.ThrowAsync<ArgumentException>(() => Phone.WriteAsync("Settings", [], Ct));
        await Should.ThrowAsync<ArgumentException>(() => Phone.RemoveAsync("LookupCache", ["x"], Ct));
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // As SQLite holds a Guid.
    private static string Key(Guid id) => id.ToString().ToUpperInvariant();
}
