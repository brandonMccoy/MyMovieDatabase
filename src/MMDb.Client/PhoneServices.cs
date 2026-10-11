using Microsoft.Extensions.DependencyInjection;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Infrastructure.Metadata;
using MMDb.Local;

namespace MMDb.Client;

public static class PhoneServices
{
    /// <summary>
    /// Everything a phone needs apart from its hardware: its own copy of the collection (see
    /// <see cref="LocalServices.AddShelfLocal"/>) and the link to the PC whose collection it is. The app adds
    /// <see cref="ILinkStore"/>, <see cref="IPcTransport"/> and <see cref="PhoneIdentity"/>.
    /// </summary>
    /// <param name="settings">Where the phone keeps its database and covers. Its keys come from the PC.</param>
    public static IServiceCollection AddShelfPhone(this IServiceCollection services, ShelfSettings settings, Uri coverBaseUri)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return services
            .AddShelfLocal(settings, coverBaseUri)
            .AddSingleton<PcLink>()
            .AddSingleton<IPcLink>(services => services.GetRequiredService<PcLink>())
            .AddSingleton<HttpCollectionApi>()
            .AddSingleton<Outbox>()
            .AddSingleton<ShelfSync>()
            .AddSingleton<IPhoneSync>(services => services.GetRequiredService<ShelfSync>())
            .AddSingleton<ICollectionApi, PhoneCollectionApi>()
            .AddSingleton<IScanQueueStore, ScanQueueStore>()
            .AddSingleton(services => new CoverShelf(services.GetRequiredService<PcLink>(), settings.CoverDirectory));
    }

    /// <summary>
    /// Creates the phone's database or brings it up to the latest schema, reads what was waiting to be sent
    /// when the app was last closed, and has the phone's own lookups use the keys its PC handed over, for as
    /// long as it holds them. Call once, before the first screen.
    /// </summary>
    public static async Task StartShelfPhoneAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.MigrateShelfDatabaseAsync(cancellationToken);
        await services.GetRequiredService<ShelfSync>().StartAsync(cancellationToken);

        var pc = services.GetRequiredService<PcLink>();
        var tmdb = services.GetRequiredService<TmdbSettings>();
        var metron = services.GetRequiredService<MetronSettings>();
        void UseKeys()
        {
            var keys = pc.Keys;
            tmdb.ApiKey = keys.TmdbApiKey;
            metron.Token = keys.MetronToken;
        }

        pc.KeysChanged += UseKeys;
        UseKeys();
    }
}
