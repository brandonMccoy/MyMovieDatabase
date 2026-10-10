using System.Text.Json;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

public sealed class ComicRepositoryTests
{
    private static readonly DateTimeOffset Jan2019 = new(2019, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Comic> SaveAsync(TestDatabase database, ComicDetails details)
    {
        var comic = Comic.Create(details, Jan2019);
        await using var db = database.NewContext();
        var repository = new EfComicRepository(db);
        repository.Add(comic);
        await repository.SaveChangesAsync(Ct);
        return comic;
    }

    private static async Task<T> UseAsync<T>(TestDatabase database, Func<EfComicRepository, Task<T>> use)
    {
        await using var db = database.NewContext();
        return await use(new EfComicRepository(db));
    }

    [Fact]
    public async Task A_saved_comic_round_trips_every_field()
    {
        using var database = await TestDatabase.MigratedAsync();
        var details = TestComics.Saga7 with { Variant = "Cover B", Printing = 2, IsKeyIssue = true, KeyNote = "1st The Will" };
        var saved = await SaveAsync(database, details);

        var loaded = await UseAsync(database, r => r.FindAsync(saved.Id, Ct));

        // As JSON: the lists come back as lists, not the arrays they went in as.
        JsonSerializer.Serialize(loaded.ShouldNotBeNull().Details).ShouldBe(JsonSerializer.Serialize(details.Normalized()));
        loaded.Barcode.ShouldBe(ComicBarcode.Parse("70985301744800711"));
        loaded.CreatedAt.ShouldBe(Jan2019);
        loaded.Version.ShouldBe(1);
    }

    [Fact]
    public async Task A_trade_round_trips_its_isbn()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = await SaveAsync(database, TestComics.SagaVolume1);

        var loaded = await UseAsync(database, r => r.FindAsync(saved.Id, Ct));

        loaded.ShouldNotBeNull().Isbn.ShouldBe(Isbn.Parse("9781607066019"));
        loaded.Format.ShouldBe(ComicFormat.TradePaperback);
        loaded.Barcode.ShouldBeNull();
    }

    [Fact]
    public async Task Comics_are_found_by_upc_in_either_form_whatever_their_add_on()
    {
        using var database = await TestDatabase.MigratedAsync();
        var cover = await SaveAsync(database, TestComics.Saga7);
        var second = await SaveAsync(database, TestComics.Saga7 with { Barcode = "70985301744800712" });
        await SaveAsync(database, TestComics.Daredevil181);

        var byUpcA = await UseAsync(database, r => r.ListByUpcAsync(Upc.Parse("709853017448"), Ct));
        var byEan = await UseAsync(database, r => r.ListByUpcAsync(Upc.Parse("0709853017448"), Ct));

        byUpcA.Select(c => c.Id).ShouldBe([cover.Id, second.Id], ignoreOrder: true);
        byEan.Select(c => c.Id).ShouldBe([cover.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Comics_are_found_by_isbn_and_by_metron_id()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saga = await SaveAsync(database, TestComics.Saga7);
        var trade = await SaveAsync(database, TestComics.SagaVolume1);

        (await UseAsync(database, r => r.ListByIsbnAsync(Isbn.Parse("1607066017"), Ct))).ShouldHaveSingleItem().Id.ShouldBe(trade.Id);
        (await UseAsync(database, r => r.ListByMetronIdsAsync([4521, 99], Ct))).ShouldHaveSingleItem().Id.ShouldBe(saga.Id);
    }

    [Fact]
    public async Task A_series_round_trips_its_issue_numbers()
    {
        using var database = await TestDatabase.MigratedAsync();
        await using (var db = database.NewContext())
        {
            var repository = new EfComicRepository(db);
            repository.AddSeries(ComicSeries.Create(1234, Jan2019, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "10", "½"])));
            await repository.SaveChangesAsync(Ct);
        }

        var series = await UseAsync(database, r => r.FindSeriesAsync(1234, Ct));

        series.ShouldNotBeNull().IssueNumbers.ShouldBe(["½", "1", "2", "10"]);
        series.Publisher.ShouldBe("Image");
        series.FetchedAt.ShouldBe(Jan2019);
        (await UseAsync(database, r => r.ListSeriesAsync(Ct))).ShouldHaveSingleItem().MetronId.ShouldBe(1234);
    }

    [Fact]
    public async Task Saving_over_a_newer_change_is_a_conflict()
    {
        using var database = await TestDatabase.MigratedAsync();
        var saved = await SaveAsync(database, TestComics.Saga7);
        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var firstRepository = new EfComicRepository(first);
        var secondRepository = new EfComicRepository(second);
        var mine = (await firstRepository.FindAsync(saved.Id, Ct))!;
        var theirs = (await secondRepository.FindAsync(saved.Id, Ct))!;

        mine.Update(TestComics.Saga7 with { Variant = "Cover A" }, Jan2019.AddDays(1));
        await firstRepository.SaveChangesAsync(Ct);
        theirs.Update(TestComics.Saga7 with { Variant = "Cover B" }, Jan2019.AddDays(2));

        await Should.ThrowAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(Ct));
    }
}
