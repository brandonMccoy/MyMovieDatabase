using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>
/// The application wired to a migrated in-memory database, fake online databases and a fake clock.
/// Each call runs in its own DI scope, like a request, so reads come from the database.
/// </summary>
internal sealed class TestShelf : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();
    private readonly ServiceProvider _services;

    private TestShelf()
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(Clock)
            .AddShelfApplication()
            .AddShelfInfrastructure(FakeOnline.Settings(_sqlite));
        _services = Online.ReplaceIn(services).BuildServiceProvider();
    }

    public FakeTimeProvider Clock { get; } = new(Start);

    public FakeOnline Online { get; } = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<TestShelf> CreateAsync()
    {
        var shelf = new TestShelf();
        await shelf._services.MigrateShelfDatabaseAsync(Ct);
        return shelf;
    }

    public Task<T> Movies<T>(Func<MovieCatalog, CancellationToken, Task<T>> use) => Use(use);

    public Task<MovieLookupResult> LookupAsync(string query) => Use<MovieLookup, MovieLookupResult>((lookup, ct) => lookup.LookupAsync(query, ct));

    private async Task<T> Use<TService, T>(Func<TService, CancellationToken, Task<T>> use)
        where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<TService>(), Ct);
    }

    /// <summary>Adds a movie that must be valid, and returns it.</summary>
    public async Task<Movie> AddAsync(MovieDetails details, params MediaFormat[] formats)
    {
        var result = await Movies((catalog, ct) => catalog.AddAsync(details, TestMovies.On(formats), ct));
        return result.ShouldBeOfType<SaveMovieResult.Saved>().Movie;
    }

    public Task<Movie?> GetAsync(Guid id) => Movies((catalog, ct) => catalog.GetAsync(id, ct));

    public Task<MovieList> ListAsync(MovieQuery? query = null) => Movies((catalog, ct) => catalog.ListAsync(query ?? new(), ct));

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }
}
