using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Local;
using MMDb.Local.Movies;
using MMDb.Server.Phone;

namespace MMDb.Server;

public static class ShelfServices
{
    /// <summary>
    /// Everything the PC needs to own the collection: what <see cref="LocalServices.AddShelfLocal"/> gives any
    /// device with a database, plus the vault, pairing with phones, and in-process <see cref="ICollectionApi"/>,
    /// <see cref="IVaultApi"/>, <see cref="IDigitalApi"/>, <see cref="IKeysApi"/> and <see cref="IPhoneApi"/> for the UI.
    /// The host registers the <see cref="Application.Metadata.IKeyStore"/> the keys set in Settings are kept in.
    /// </summary>
    /// <param name="coverBaseUri">Where the UI loads stored covers from; see <see cref="CoverLinks"/>.</param>
    public static IServiceCollection AddShelf(this IServiceCollection services, ShelfSettings settings, Uri coverBaseUri) =>
        services
            .AddShelfLocal(settings, coverBaseUri)
            .AddSingleton<ICollectionApi>(services => services.GetRequiredService<InProcessCollectionApi>())
            .AddSingleton<IVaultApi, InProcessVaultApi>()
            .AddSingleton<IDigitalApi, InProcessDigitalApi>()
            .AddSingleton<IKeysApi, InProcessKeysApi>()
            .AddSingleton<PhoneServer>()
            .AddSingleton<DeviceGate>()
            .AddSingleton<IPhoneApi, InProcessPhoneApi>();
}
