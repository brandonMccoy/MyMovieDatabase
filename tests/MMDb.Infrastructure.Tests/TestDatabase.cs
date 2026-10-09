using Microsoft.EntityFrameworkCore;
using MMDb.Infrastructure.Persistence;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>A migrated in-memory database; <see cref="NewContext"/> gives an independent unit of work.</summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    private TestDatabase()
    {
    }

    public static async Task<TestDatabase> MigratedAsync()
    {
        var database = new TestDatabase();
        await using var db = database.NewContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }

    public ShelfDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ShelfDbContext>().UseSqlite(_sqlite.ConnectionString).Options);

    public void Dispose() => _sqlite.Dispose();
}
