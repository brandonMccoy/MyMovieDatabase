using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMDb.Application.Movies;
using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure;

public static class InfrastructureServices
{
    /// <summary>Connection string for the database file at <paramref name="databasePath"/>.</summary>
    public static string SqliteConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    /// <summary>Registers the SQLite database and the repositories over it.</summary>
    /// <param name="connectionString">See <see cref="SqliteConnectionString"/>.</param>
    public static IServiceCollection AddShelfInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ShelfDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IMovieRepository, EfMovieRepository>();
        return services;
    }

    /// <summary>Creates the database or brings it up to the latest schema.</summary>
    public static async Task MigrateShelfDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShelfDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
