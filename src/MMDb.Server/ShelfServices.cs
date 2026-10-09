using MMDb.Application;
using MMDb.Contracts;
using MMDb.Infrastructure;

namespace MMDb.Server;

public static class ShelfServices
{
    /// <summary>
    /// Everything the PC needs to own the collection: use cases, the SQLite database and an
    /// in-process <see cref="ICollectionApi"/> for the UI.
    /// </summary>
    /// <param name="connectionString">See <see cref="InfrastructureServices.SqliteConnectionString"/>.</param>
    public static IServiceCollection AddShelf(this IServiceCollection services, string connectionString) =>
        services
            .AddShelfApplication()
            .AddShelfInfrastructure(connectionString)
            .AddSingleton<ICollectionApi, InProcessCollectionApi>();
}
