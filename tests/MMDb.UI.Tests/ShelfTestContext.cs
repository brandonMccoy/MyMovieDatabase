using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Infrastructure;
using MMDb.Server;
using MMDb.Testing;

namespace MMDb.UI.Tests;

/// <summary>bUnit context with the PC's real collection (in-memory SQLite) behind <see cref="ICollectionApi"/>.</summary>
public abstract class ShelfTestContext : BunitContext, IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();

    protected ShelfTestContext()
    {
        Services.AddShelf(_sqlite.ConnectionString);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddShelfUi();
    }

    protected FakeTimeProvider Clock { get; } = new(Start);

    protected ICollectionApi Api => Services.GetRequiredService<ICollectionApi>();

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    protected static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    protected static SaveMovieRequest Heat => new()
    {
        Title = "Heat",
        Year = 1995,
        Directors = ["Michael Mann"],
        Cast = [new("Al Pacino", "Vincent Hanna"), new("Robert De Niro", "Neil McCauley")],
        Genres = ["Crime", "Drama"],
        RuntimeMinutes = 170,
        Rating = "R",
        Overview = "A detective and a thief circle each other across Los Angeles.",
        Formats = [new(MovieFormatKind.Uhd4K, Upc: "786936894424"), new(MovieFormatKind.Dvd)],
    };

    protected static SaveMovieRequest Film(string title, int? year = null, params MovieFormatKind[] formats) => new()
    {
        Title = title,
        Year = year,
        Formats = [.. (formats.Length == 0 ? [MovieFormatKind.Dvd] : formats).Select(f => new FormatRequest(f))],
    };

    public async ValueTask InitializeAsync() => await Services.MigrateShelfDatabaseAsync(Ct);

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        _sqlite.Dispose();
        GC.SuppressFinalize(this);
    }

    protected async Task<MovieDto> SeedAsync(SaveMovieRequest request)
    {
        var response = await Api.AddMovieAsync(request, Ct);
        response.Status.ShouldBe(SaveStatus.Saved, string.Join("; ", response.Errors));
        return response.Movie!;
    }

    /// <summary>The current location relative to the app root, e.g. <c>movies/…</c> or <c>?q=heat</c>.</summary>
    protected string RelativeLocation => Navigation.ToBaseRelativePath(Navigation.Uri);
}
