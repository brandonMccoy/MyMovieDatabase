using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Migrating_twice_is_harmless()
    {
        using var sqlite = new InMemorySqlite();
        using var services = new ServiceCollection()
            .AddShelfInfrastructure(sqlite.ConnectionString)
            .BuildServiceProvider();

        await services.MigrateShelfDatabaseAsync(TestContext.Current.CancellationToken);
        await services.MigrateShelfDatabaseAsync(TestContext.Current.CancellationToken);
    }
}
