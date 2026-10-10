using Microsoft.Extensions.Logging;
using MMDb.UI;
#if WINDOWS
using Microsoft.Extensions.Configuration;
using MMDb.Contracts;
using MMDb.Infrastructure;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Server;
#endif

namespace MMDb.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
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
#endif

        return app;
    }
}
