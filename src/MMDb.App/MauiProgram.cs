using Microsoft.Extensions.Logging;
using MMDb.UI;
#if WINDOWS
using Microsoft.Extensions.Configuration;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Local;
using MMDb.Server;
using MMDb.Server.Keys;
using MMDb.Server.Phone;
#elif ANDROID
using BarcodeScanning;
using MMDb.Client;
using MMDb.Contracts;
using MMDb.Infrastructure;
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
        // The phone pairs with the PC once and keeps its own copy of the PC's collection, in the app's own
        // files (not its cache, which Android may empty). The keys for the online databases come from the PC.
        builder.UseBarcodeScanning();
        var phoneData = FileSystem.AppDataDirectory;
        builder.Services.AddSingleton(ShelfDevice.Phone);
        builder.Services.AddSingleton<ILinkStore, SecureLinkStore>();
        builder.Services.AddSingleton<IPcTransport, TlsTransport>();
        builder.Services.AddSingleton(new PhoneIdentity(PhoneName()));
        builder.Services.AddShelfPhone(
            new ShelfSettings
            {
                ConnectionString = InfrastructureServices.SqliteConnectionString(Path.Combine(phoneData, "shelf.db")),
                CoverDirectory = Path.Combine(phoneData, "covers"),
            },
            CoverFolder.BaseUri);
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

        // API keys are set in Settings and kept beside the database, encrypted by Windows; a developer's user
        // secrets apply until then (see docs/ARCHITECTURE.md).
        builder.Configuration.AddUserSecrets(typeof(MauiProgram).Assembly, optional: true);
        builder.Services.AddSingleton<IKeyStore>(new ProtectedKeyFile(Path.Combine(dataDirectory, "keys.dat")));
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
        builder.Services.AddSingleton<IVideoFilePicker, WindowsFilePicker>();
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

        // Before anything is looked up.
        app.Services.GetRequiredService<IKeysApi>().Load();

        // Catch the vault up in the background; how it went shows in the sidebar.
        _ = Task.Run(() => app.Services.GetRequiredService<IVaultApi>().StartAsync());

        // Read the movie folder for files that arrived, moved or went while Shelf was closed, then watch it.
        // TMDB isn't asked until the import screen is opened.
        _ = Task.Run(() => app.Services.GetRequiredService<IDigitalApi>().StartAsync());

        // Covers the wishlist is still missing (wanted while offline) are tried for again; they show up when they arrive.
        _ = Task.Run(() => ((InProcessCollectionApi)app.Services.GetRequiredService<ICollectionApi>()).FetchMissingWishlistCoversAsync());

        // Listen for paired phones; Settings shows whether that worked. The PC's certificate is kept beside the database.
        _ = Task.Run(() => new PhoneHost(app.Services).StartAsync(Path.Combine(dataDirectory, "phone.pfx")));
#elif ANDROID
        // Off the UI thread's synchronization context, so blocking here can't deadlock.
        Task.Run(() => app.Services.StartShelfPhoneAsync()).GetAwaiter().GetResult();

        // Back on a network (perhaps the one at home) with Shelf still on screen: nothing else would notice.
        var sync = app.Services.GetRequiredService<ShelfSync>();
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess != NetworkAccess.None)
            {
                _ = sync.NetworkChangedAsync();
            }
        };
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
