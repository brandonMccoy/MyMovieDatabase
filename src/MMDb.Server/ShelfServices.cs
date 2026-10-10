using MMDb.Application;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Server.Movies;

namespace MMDb.Server;

public static class ShelfServices
{
    /// <summary>
    /// Everything the PC needs to own the collection: use cases, the SQLite database, covers, the online
    /// databases, the vault, and in-process <see cref="ICollectionApi"/> and <see cref="IVaultApi"/> for the UI.
    /// </summary>
    /// <param name="coverBaseUri">Where the UI loads stored covers from; see <see cref="CoverLinks"/>.</param>
    public static IServiceCollection AddShelf(this IServiceCollection services, ShelfSettings settings, Uri coverBaseUri) =>
        services
            .AddShelfApplication()
            .AddShelfInfrastructure(settings)
            .AddSingleton(new CoverLinks(coverBaseUri))
            .AddSingleton<ICollectionApi, InProcessCollectionApi>()
            .AddSingleton<IVaultApi, InProcessVaultApi>();
}
