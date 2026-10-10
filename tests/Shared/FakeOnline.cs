using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Infrastructure;

namespace MMDb.Testing;

/// <summary>Hand-written stand-ins for TMDB, UPCitemdb, Metron, Open Library, the image servers and the cover folder.</summary>
internal sealed class FakeOnline
{
    public FakeTmdb Tmdb { get; } = new();

    public FakeBarcodes Barcodes { get; } = new();

    public FakePosters Posters { get; } = new();

    public FakeCoverStore Covers { get; } = new();

    public FakeMetron Metron { get; } = new();

    public FakeBooks Books { get; } = new();

    public FakeComicCovers ComicCovers { get; } = new();

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
        .Replace(ServiceDescriptor.Singleton<ICoverStore>(Covers))
        .Replace(ServiceDescriptor.Singleton<IComicMetadata>(Metron))
        .Replace(ServiceDescriptor.Singleton<IBookMetadata>(Books))
        .Replace(ServiceDescriptor.Singleton<ICoverSource>(ComicCovers));
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

    private readonly List<string> _fetched = [];

    public IReadOnlyList<string> Fetched { get { lock (_fetched) { return [.. _fetched]; } } }

    public static byte[] ImageFor(string posterPath) => System.Text.Encoding.UTF8.GetBytes("image of " + posterPath);

    public Task<byte[]?> FetchAsync(string posterPath, CancellationToken cancellationToken = default)
    {
        lock (_fetched)
        {
            _fetched.Add(posterPath);
        }

        return Task.FromResult(Offline ? null : ImageFor(posterPath));
    }
}

internal sealed class FakeCoverStore : ICoverStore
{
    /// <summary>Covers may be saved several at a time.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public Task SaveAsync(string name, byte[] image, CancellationToken cancellationToken = default)
    {
        Files[name] = image;
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Files.GetValueOrDefault(name));

    public void Delete(string name) => Files.TryRemove(name, out _);
}

/// <summary>Finds issues the way Metron does: by exact barcode, barcode prefix, or series words, year and number.</summary>
internal sealed class FakeMetron : IComicMetadata
{
    private readonly List<IssueFacts> _issues = [];
    private readonly Dictionary<int, SeriesFacts> _series = [];
    private readonly List<IssueSearch> _searches = [];
    private int _seriesFetches;

    public MetadataProblem? FailWith { get; set; }

    public IReadOnlyList<IssueSearch> Searches { get { lock (_searches) { return [.. _searches]; } } }

    public int SeriesFetches => Volatile.Read(ref _seriesFetches);

    /// <param name="issue">Must have a <see cref="ComicDetails.MetronId"/>.</param>
    public FakeMetron Add(ComicDetails issue, params VariantFacts[] variants)
    {
        _issues.Add(new IssueFacts(issue.Normalized(), variants));
        return this;
    }

    public FakeMetron AddSeries(int metronSeriesId, SeriesFacts series)
    {
        _series[metronSeriesId] = series;
        return this;
    }

    public Task<IReadOnlyList<int>> SearchAsync(IssueSearch search, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        lock (_searches)
        {
            _searches.Add(search);
        }

        IReadOnlyList<int> ids = [.. _issues.Where(i => Matches(i, search)).Select(i => i.Details.MetronId!.Value)];
        return Task.FromResult(ids);
    }

    public Task<IssueFacts?> GetIssueAsync(int metronId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult(_issues.Find(i => i.Details.MetronId == metronId));
    }

    public Task<SeriesFacts?> GetSeriesAsync(int metronSeriesId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Interlocked.Increment(ref _seriesFetches);
        return Task.FromResult(_series.GetValueOrDefault(metronSeriesId));
    }

    /// <summary>The issues added with <see cref="Add"/> that belong to the series.</summary>
    public Task<IReadOnlyList<IssueListing>> ListIssuesAsync(int metronSeriesId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        IReadOnlyList<IssueListing> issues = [.. _issues
            .Where(i => i.Details.MetronSeriesId == metronSeriesId)
            .Select(i => new IssueListing(i.Details.MetronId!.Value, i.Details.Number!, i.Details.CoverUrl))];
        return Task.FromResult(issues);
    }

    private static bool Matches(IssueFacts issue, IssueSearch search)
    {
        var d = issue.Details;
        if (search.Barcode is { } barcode)
        {
            return d.Barcode == barcode;
        }

        if (search.UpcPrefix is { } prefix && d.Barcode?.StartsWith(prefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        if (search.Series is { } words && !words.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => d.Series?.Contains(w, StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        return (search.SeriesYear is null || d.SeriesYear == search.SeriesYear) && (search.Number is null || d.Number == search.Number);
    }

    private void ThrowIfFailing()
    {
        if (FailWith is { } problem)
        {
            throw new MetadataException("Metron", problem);
        }
    }
}

internal sealed class FakeBooks : IBookMetadata
{
    private readonly Dictionary<string, ComicDetails> _books = [];

    public MetadataProblem? FailWith { get; set; }

    public int Lookups { get; private set; }

    public FakeBooks Add(ComicDetails book)
    {
        _books[Isbn.Parse(book.Isbn!).Value] = book;
        return this;
    }

    public Task<ComicDetails?> FindByIsbnAsync(Isbn isbn, CancellationToken cancellationToken = default)
    {
        Lookups++;
        if (FailWith is { } problem)
        {
            throw new MetadataException("Open Library", problem);
        }

        return Task.FromResult(_books.GetValueOrDefault(isbn.Value));
    }
}

/// <summary>Serves a few bytes per cover link, or nothing while <see cref="Offline"/>.</summary>
internal sealed class FakeComicCovers : ICoverSource
{
    public bool Offline { get; set; }

    private readonly List<string> _fetched = [];

    public IReadOnlyList<string> Fetched { get { lock (_fetched) { return [.. _fetched]; } } }

    public static byte[] ImageFor(string url) => System.Text.Encoding.UTF8.GetBytes("image of " + url);

    public Task<byte[]?> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        lock (_fetched)
        {
            _fetched.Add(url);
        }

        return Task.FromResult(Offline ? null : ImageFor(url));
    }
}
