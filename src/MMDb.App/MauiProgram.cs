using Microsoft.Extensions.Logging;
using MMDb.UI;
#if WINDOWS
using Microsoft.Extensions.Configuration;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Server;
using MMDb.Server.Phone;
#elif ANDROID
using BarcodeScanning;
using MMDb.Client;
using MMDb.Contracts;
using MMDb.UI.Scanning;
#endif

namespace MMDb.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if ANDROID
        // The phone is a client of the PC: it pairs once, then asks the PC for everything over the home network.
        builder.UseBarcodeScanning();
        builder.Services.AddSingleton(ShelfDevice.Phone);
        builder.Services.AddSingleton<ILinkStore, SecureLinkStore>();
        builder.Services.AddSingleton<IPcTransport, TlsTransport>();
        builder.Services.AddSingleton(new PhoneIdentity(PhoneName()));
        builder.Services.AddSingleton<PcLink>();
        builder.Services.AddSingleton<IPcLink>(services => services.GetRequiredService<PcLink>());
        builder.Services.AddSingleton<ICollectionApi, HttpCollectionApi>();
        builder.Services.AddSingleton(services => new CoverShelf(services.GetRequiredService<PcLink>(), Path.Combine(FileSystem.CacheDirectory, "covers")));
        builder.Services.AddSingleton<CameraScanner>();
        builder.Services.AddSingleton<IBarcodeScanner>(services => services.GetRequiredService<CameraScanner>());
#endif
        builder.Services.AddShelfUi();

#if WINDOWS
        // %LOCALAPPDATA%\Shelf\shelf.db, with startup backups and downloaded covers beside it. SHELF_DATA_DIR
        // points elsewhere (a scratch collection for trying things out or for E2E tests).
        var dataDirectory = Environment.GetEnvironmentVariable("SHELF_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shelf");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "shelf.db");
        CoverFolder.Path = Path.Combine(dataDirectory, "covers");

        // API keys stay on this PC, in user secrets (see docs/ARCHITECTURE.md).
        builder.Configuration.AddUserSecrets(typeof(MauiProgram).Assembly, optional: true);
        builder.Services.AddShelf(
            new ShelfSettings
            {
                ConnectionString = InfrastructureServices.SqliteConnectionString(databasePath),
                CoverDirectory = CoverFolder.Path,
                Tmdb = new TmdbSettings { ApiKey = builder.Configuration["Tmdb:ApiKey"] },
                Metron = new MetronSettings { Token = builder.Configuration["Metron:Token"] },
            },
            CoverFolder.BaseUri);
        builder.Services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

#if WINDOWS
        DatabaseBackup.Run(databasePath, Path.Combine(dataDirectory, "backups"), TimeProvider.System);
        // Off the UI thread's synchronization context, so blocking here can't deadlock.
        Task.Run(() => app.Services.MigrateShelfDatabaseAsync()).GetAwaiter().GetResult();

        // Catch the vault up in the background; how it went shows in the sidebar.
        _ = Task.Run(() => app.Services.GetRequiredService<IVaultApi>().StartAsync());

        // Covers the wishlist is still missing (wanted while offline) are tried for again; they show up when they arrive.
        _ = Task.Run(() => ((InProcessCollectionApi)app.Services.GetRequiredService<ICollectionApi>()).FetchMissingWishlistCoversAsync());

        // Listen for paired phones; Settings shows whether that worked. The PC's certificate is kept beside the database.
        _ = Task.Run(() => new PhoneHost(app.Services).StartAsync(Path.Combine(dataDirectory, "phone.pfx")));
#endif

        return app;
    }

#if ANDROID
    // What the PC lists this phone as: the name its owner gave it, else its model.
    private static string PhoneName()
    {
        var name = DeviceInfo.Current.Name;
        return !string.IsNullOrWhiteSpace(name) ? name : $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
    }
#endif
}
