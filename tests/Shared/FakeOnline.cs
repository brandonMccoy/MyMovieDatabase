using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Domain.Movies;
using MMDb.Infrastructure;

namespace MMDb.Testing;

/// <summary>Hand-written stand-ins for TMDB, UPCitemdb, the poster server and the cover folder.</summary>
internal sealed class FakeOnline
{
    public FakeTmdb Tmdb { get; } = new();

    public FakeBarcodes Barcodes { get; } = new();

    public FakePosters Posters { get; } = new();

    public FakeCoverStore Covers { get; } = new();

    /// <summary>Settings for an in-memory database; the cover folder is never touched because the store is faked.</summary>
    public static ShelfSettings Settings(InMemorySqlite sqlite) => new()
    {
        ConnectionString = sqlite.ConnectionString,
        CoverDirectory = Path.Combine(Path.GetTempPath(), "shelf-tests-unused"),
    };

    /// <summary>Swaps the real online databases and cover folder for these fakes.</summary>
    public IServiceCollection ReplaceIn(IServiceCollection services) => services
        .Replace(ServiceDescriptor.Singleton<IMovieMetadata>(Tmdb))
        .Replace(ServiceDescriptor.Singleton<IBarcodeLookup>(Barcodes))
        .Replace(ServiceDescriptor.Singleton<IPosterSource>(Posters))
        .Replace(ServiceDescriptor.Singleton<ICoverStore>(Covers));
}

/// <summary>Finds films whose title contains the search text (ignoring case), in the order they were added.</summary>
internal sealed class FakeTmdb : IMovieMetadata
{
    private readonly List<MovieDetails> _films = [];
    private readonly List<(string Title, int? Year)> _searches = [];
    private int _fetches;

    public MetadataProblem? FailWith { get; set; }

    public IReadOnlyList<(string Title, int? Year)> Searches { get { lock (_searches) { return [.. _searches]; } } }

    public int Fetches => Volatile.Read(ref _fetches);

    /// <param name="film">Must have a <see cref="MovieDetails.TmdbId"/>.</param>
    public FakeTmdb Add(MovieDetails film)
    {
        _films.Add(film);
        return this;
    }

    public Task<IReadOnlyList<int>> SearchAsync(string title, int? year, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        lock (_searches)
        {
            _searches.Add((title, year));
        }

        IReadOnlyList<int> ids = [.. _films
            .Where(f => f.Title.Contains(title, StringComparison.OrdinalIgnoreCase) && (year is null || f.Year == year))
            .Select(f => f.TmdbId!.Value)];
        return Task.FromResult(ids);
    }

    public Task<MovieDetails?> GetAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Interlocked.Increment(ref _fetches);
        return Task.FromResult(_films.Find(f => f.TmdbId == tmdbId));
    }

    private void ThrowIfFailing()
    {
        if (FailWith is { } problem)
        {
            throw new MetadataException("TMDB", problem);
        }
    }
}

internal sealed class FakeBarcodes : IBarcodeLookup
{
    private readonly Dictionary<string, string[]> _products = [];

    public MetadataProblem? FailWith { get; set; }

    public int Lookups { get; private set; }

    public FakeBarcodes Add(string upc, params string[] productNames)
    {
        _products[upc] = productNames;
        return this;
    }

    public Task<IReadOnlyList<string>> FindProductNamesAsync(Upc upc, CancellationToken cancellationToken = default)
    {
        Lookups++;
        if (FailWith is { } problem)
        {
            throw new MetadataException("UPCitemdb", problem);
        }

        return Task.FromResult<IReadOnlyList<string>>(_products.GetValueOrDefault(upc.Value, []));
    }
}

/// <summary>Serves a few bytes per poster path, or nothing while <see cref="Offline"/>.</summary>
internal sealed class FakePosters : IPosterSource
{
    public bool Offline { get; set; }

    public List<string> Fetched { get; } = [];

    public static byte[] ImageFor(string posterPath) => System.Text.Encoding.UTF8.GetBytes("image of " + posterPath);

    public Task<byte[]?> FetchAsync(string posterPath, CancellationToken cancellationToken = default)
    {
        Fetched.Add(posterPath);
        return Task.FromResult(Offline ? null : ImageFor(posterPath));
    }
}

internal sealed class FakeCoverStore : ICoverStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task SaveAsync(string name, byte[] image, CancellationToken cancellationToken = default)
    {
        Files[name] = image;
        return Task.CompletedTask;
    }

    public void Delete(string name) => Files.Remove(name);
}
