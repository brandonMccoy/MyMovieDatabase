using MMDb.Application;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Local.Movies;

namespace MMDb.Local;

public static class LocalServices
{
    /// <summary>
    /// A collection kept on this device: use cases, the SQLite database, covers, the online databases, and
    /// in-process <see cref="ICollectionApi"/>, <see cref="IStatsApi"/> and <see cref="ITypesApi"/> for the UI. The PC's is the one true
    /// copy; a phone's is its own copy of it.
    /// </summary>
    /// <param name="coverBaseUri">Where the UI loads stored covers from; see <see cref="CoverLinks"/>.</param>
    public static IServiceCollection AddShelfLocal(this IServiceCollection services, ShelfSettings settings, Uri coverBaseUri) =>
        services
            .AddShelfApplication()
            .AddShelfInfrastructure(settings)
            .AddSingleton(new CoverLinks(coverBaseUri))
            .AddSingleton<InProcessCollectionApi>()
            .AddSingleton<IStatsApi, InProcessStatsApi>()
            .AddSingleton<ITypesApi, InProcessTypesApi>();
}
