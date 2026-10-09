using Microsoft.Extensions.Logging;
using MMDb.UI;
#if WINDOWS
using MMDb.Infrastructure;
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
        // %LOCALAPPDATA%\Shelf\shelf.db, with startup backups beside it. SHELF_DATA_DIR points elsewhere
        // (a scratch collection for trying things out or for E2E tests).
        var dataDirectory = Environment.GetEnvironmentVariable("SHELF_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shelf");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "shelf.db");
        builder.Services.AddShelf(InfrastructureServices.SqliteConnectionString(databasePath));
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
#endif

        return app;
    }
}
