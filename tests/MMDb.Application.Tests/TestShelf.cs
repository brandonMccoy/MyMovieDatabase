using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Movies;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Application.Tests;

/// <summary>
/// The application wired to a migrated in-memory database and a fake clock.
/// Each call runs in its own DI scope, like a request, so reads come from the database.
/// </summary>
internal sealed class TestShelf : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();
    private readonly ServiceProvider _services;

    private TestShelf()
    {
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(Clock)
            .AddShelfApplication()
            .AddShelfInfrastructure(_sqlite.ConnectionString)
            .BuildServiceProvider();
    }

    public FakeTimeProvider Clock { get; } = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<TestShelf> CreateAsync()
    {
        var shelf = new TestShelf();
        await shelf._services.MigrateShelfDatabaseAsync(Ct);
        return shelf;
    }

    public async Task<T> Movies<T>(Func<MovieCatalog, CancellationToken, Task<T>> use)
    {
        await using var scope = _services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<MovieCatalog>(), Ct);
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
