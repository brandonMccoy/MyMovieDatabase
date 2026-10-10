using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MMDb.Domain.Comics;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task Migrations_create_the_schema_on_an_empty_database()
    {
        using var database = await TestDatabase.MigratedAsync();
        await using var db = database.NewContext();

        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await db.Movies.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task The_model_has_no_changes_missing_from_the_migrations()
    {
        using var database = await TestDatabase.MigratedAsync();
        await using var db = database.NewContext();

        db.Database.HasPendingModelChanges().ShouldBeFalse("run: dotnet ef migrations add <Name> --project src/MMDb.Infrastructure");
    }

    [Fact]
    public async Task A_run_of_missing_issues_once_saved_as_one_wish_becomes_a_wish_per_issue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sqlite = new InMemorySqlite();
        var options = new DbContextOptionsBuilder<ShelfDbContext>().UseSqlite(sqlite.ConnectionString).Options;
        await using (var before = new ShelfDbContext(options))
        {
            await before.GetService<IMigrator>().MigrateAsync("WishlistComics", ct);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO WishlistComics (Id, Format, Series, SeriesYear, Numbers, Title, Publisher, MetronId, MetronSeriesId, CoverUrl, FromMissing, CreatedAt, UpdatedAt, Version, CoverImage) VALUES
                ('01A12774-625F-758B-810D-65880BABF628', 'SingleIssue', 'Saga', 2012, '["2","3","4"]', NULL, 'Image Comics', NULL, 1234, NULL, 1, '2026-10-10 20:00:00+00:00', '2026-10-10 20:00:00+00:00', 3, NULL),
                ('01A12780-193C-79F0-8498-12B1B74124E6', 'SingleIssue', 'Batman', 1940, '["300"]', NULL, 'DC', 77, 5, 'https://static.metron.cloud/b300.jpg', 0, '2026-10-09 08:00:00+00:00', '2026-10-09 08:00:00+00:00', 1, 'b300.jpg'),
                ('01A12780-2222-79F0-8498-12B1B74124E6', 'Hardcover', NULL, NULL, '[]', 'Saga: Book One', NULL, NULL, NULL, NULL, 0, '2026-10-09 08:00:00+00:00', '2026-10-09 08:00:00+00:00', 1, NULL);
                """,
                ct);
        }

        await using var db = new ShelfDbContext(options);
        await db.Database.MigrateAsync(ct);

        var wishes = await db.WishlistComics.AsNoTracking().ToListAsync(ct);
        wishes.Select(w => w.DisplayName).Order().ShouldBe(["Batman #300", "Saga #2", "Saga #3", "Saga #4", "Saga: Book One"]);
        wishes.Select(w => w.Id).Distinct().Count().ShouldBe(5);
        wishes.Where(w => w.Series == "Saga").ShouldAllBe(w =>
            w.FromMissing && w.SeriesYear == 2012 && w.Publisher == "Image Comics" && w.MetronSeriesId == 1234 && w.Format == ComicFormat.SingleIssue
            && w.CreatedAt == new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        wishes.Single(w => w.Number == "2").Id.ShouldBe(Guid.Parse("01A12774-625F-758B-810D-65880BABF628"), "the first issue keeps the row");
        wishes.Single(w => w.Series == "Batman").ShouldSatisfyAllConditions(
            w => w.Number.ShouldBe("300"),
            w => w.MetronId.ShouldBe(77),
            w => w.CoverImage.ShouldBe("b300.jpg"),
            w => w.FromMissing.ShouldBeFalse());
        wishes.Single(w => w.Title is not null).Number.ShouldBeNull();
    }

    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        using var sqlite = new InMemorySqlite();
        using var services = new ServiceCollection()
            .AddShelfInfrastructure(FakeOnline.Settings(sqlite))
            .BuildServiceProvider();

        await services.MigrateShelfDatabaseAsync(TestContext.Current.CancellationToken);
        await services.MigrateShelfDatabaseAsync(TestContext.Current.CancellationToken);
    }
}
