using MMDb.Application;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Server.Movies;
using MMDb.Server.Phone;

namespace MMDb.Server;

public static class ShelfServices
{
    /// <summary>
    /// Everything the PC needs to own the collection: use cases, the SQLite database, covers, the online
    /// databases, the vault, pairing with phones, and in-process <see cref="ICollectionApi"/>, <see cref="IVaultApi"/>,
    /// <see cref="IDigitalApi"/> and <see cref="IPhoneApi"/> for the UI.
    /// </summary>
    /// <param name="coverBaseUri">Where the UI loads stored covers from; see <see cref="CoverLinks"/>.</param>
    public static IServiceCollection AddShelf(this IServiceCollection services, ShelfSettings settings, Uri coverBaseUri) =>
        services
            .AddShelfApplication()
            .AddShelfInfrastructure(settings)
            .AddSingleton(new CoverLinks(coverBaseUri))
            .AddSingleton<InProcessCollectionApi>()
            .AddSingleton<ICollectionApi>(services => services.GetRequiredService<InProcessCollectionApi>())
            .AddSingleton<IVaultApi, InProcessVaultApi>()
            .AddSingleton<IDigitalApi, InProcessDigitalApi>()
            .AddSingleton<PhoneServer>()
            .AddSingleton<DeviceGate>()
            .AddSingleton<IPhoneApi, InProcessPhoneApi>();
}
