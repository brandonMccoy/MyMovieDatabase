using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MMDb.Application.Comics;
using MMDb.Application.Devices;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Application.Vault;
using MMDb.Application.Wishlist;
using MMDb.Infrastructure.Covers;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Infrastructure.Vault;

namespace MMDb.Infrastructure;

/// <summary>Where the PC keeps its data, and how it reaches the online databases.</summary>
public sealed class ShelfSettings
{
    /// <summary>See <see cref="InfrastructureServices.SqliteConnectionString"/>.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Folder for downloaded covers.</summary>
    public required string CoverDirectory { get; init; }

    public TmdbSettings Tmdb { get; init; } = new();

    public MetronSettings Metron { get; init; } = new();
}

public static partial class InfrastructureServices
{
    private static readonly ProductInfoHeaderValue UserAgent = new("Shelf", "1.0");

    /// <summary>Connection string for the database file at <paramref name="databasePath"/>.</summary>
    public static string SqliteConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    /// <summary>Registers the SQLite database, the repositories over it, the cover folder, the vault writer, settings and the online databases.</summary>
    public static IServiceCollection AddShelfInfrastructure(this IServiceCollection services, ShelfSettings settings)
    {
        // Singleton options so the lookup cache (used by long-lived HTTP handlers) can make its own contexts.
        services.AddDbContext<ShelfDbContext>(options => options.UseSqlite(settings.ConnectionString), optionsLifetime: ServiceLifetime.Singleton);
        services.AddScoped<IMovieRepository, EfMovieRepository>();
        services.AddScoped<IComicRepository, EfComicRepository>();
        services.AddScoped<IDeviceRepository, EfDeviceRepository>();
        services.AddScoped<IWishlistRepository, EfWishlistRepository>();
        services.AddSingleton<LookupCache>();
        services.AddSingleton<ICoverStore>(new FileCoverStore(settings.CoverDirectory));
        services.AddSingleton<ISettingStore, SettingStore>();
        services.AddSingleton<IVaultWriter>(sp => new FileVaultWriter(settings.CoverDirectory, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(settings.Tmdb);
        services.AddSingleton(settings.Metron);

        services.AddHttpClient<IMovieMetadata, TmdbClient>(http => Configure(http, "https://api.themoviedb.org/3/"))
            .AddHttpMessageHandler(sp => CacheHandler(sp, TmdbClient.Source, uri =>
                uri.AbsolutePath.Contains("/search/", StringComparison.Ordinal) ? TimeSpan.FromDays(1) : TimeSpan.FromDays(30)))
            .AddStandardResilienceHandler(Resilience);

        services.AddHttpClient<IBarcodeLookup, UpcItemDbClient>(http => Configure(http, "https://api.upcitemdb.com/"))
            .AddHttpMessageHandler(sp => CacheHandler(sp, UpcItemDbClient.Source, _ => TimeSpan.FromDays(30)))
            .AddStandardResilienceHandler(NoRetryWhenBusy);

        // An issue's details hardly change; searches and a series' issue list grow as new issues come out.
        services.AddHttpClient<IComicMetadata, MetronClient>(http => Configure(http, "https://metron.cloud/api/"))
            .AddHttpMessageHandler(sp => CacheHandler(sp, MetronClient.Source, uri =>
                IssueDetails().IsMatch(uri.AbsolutePath) ? TimeSpan.FromDays(30) : TimeSpan.FromDays(1)))
            .AddStandardResilienceHandler(NoRetryWhenBusy);

        services.AddHttpClient<IBookMetadata, OpenLibraryClient>(http =>
            {
                Configure(http, "https://openlibrary.org/");
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(personal collection app)"));
            })
            .AddHttpMessageHandler(sp => CacheHandler(sp, OpenLibraryClient.Source, _ => TimeSpan.FromDays(30)))
            .AddStandardResilienceHandler(NoRetryWhenBusy);

        services.AddHttpClient<ICoverSource, ComicCovers>(http => http.DefaultRequestHeaders.UserAgent.Add(UserAgent))
            .AddStandardResilienceHandler(Resilience);

        services.AddHttpClient<IPosterSource, TmdbPosters>(http => http.DefaultRequestHeaders.UserAgent.Add(UserAgent))
            .AddStandardResilienceHandler(Resilience);

        return services;
    }

    /// <summary>Creates the database or brings it up to the latest schema.</summary>
    public static async Task MigrateShelfDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await db.IndexPeopleAsync(cancellationToken);
    }

    private static void Configure(HttpClient http, string baseAddress)
    {
        http.BaseAddress = new Uri(baseAddress);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.Add(UserAgent);
    }

    // Someone is waiting on the search screen: give up after 20 s rather than the default 30 s.
    private static void Resilience(HttpStandardResilienceOptions options) =>
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);

    // Their limits are per minute and per day, so retrying a 429 within seconds only burns lookups.
    private static void NoRetryWhenBusy(HttpStandardResilienceOptions options)
    {
        Resilience(options);
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException || (int?)args.Outcome.Result?.StatusCode >= 500);
    }

    [GeneratedRegex(@"^/api/issue/\d+/?$")]
    private static partial Regex IssueDetails();

    private static LookupCacheHandler CacheHandler(IServiceProvider services, string provider, Func<Uri, TimeSpan> maxAge) =>
        new(provider, maxAge, services.GetRequiredService<LookupCache>(), services.GetRequiredService<TimeProvider>());
}
