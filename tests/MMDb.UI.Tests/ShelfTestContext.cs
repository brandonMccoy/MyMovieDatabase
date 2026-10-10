using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Vault;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Domain.Comics;
using MMDb.Infrastructure;
using MMDb.Server;
using MMDb.Testing;

namespace MMDb.UI.Tests;

/// <summary>
/// bUnit context with the PC's real collection (in-memory SQLite) behind <see cref="ICollectionApi"/>,
/// and fakes for the online databases, the cover folder, the vault writer and the folder dialog.
/// </summary>
public abstract class ShelfTestContext : BunitContext, IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();

    protected ShelfTestContext()
    {
        Services.AddShelf(FakeOnline.Settings(_sqlite), CoverBase);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddShelfUi();
        Online.ReplaceIn(Services);
        Services.Replace(ServiceDescriptor.Singleton<IVaultWriter>(Vault));
        Services.AddSingleton<IFolderPicker>(FolderPicker);
        Services.AddSingleton<IVideoFilePicker>(FilePicker);
    }

    protected static readonly Uri CoverBase = new("https://covers.test/");

    protected FakeTimeProvider Clock { get; } = new(Start);

    private protected FakeOnline Online { get; } = new();

    private protected FakeVault Vault { get; } = new();

    protected FakeFolderPicker FolderPicker { get; } = new();

    protected FakeFilePicker FilePicker { get; } = new();

    protected IDigitalApi Digital => Services.GetRequiredService<IDigitalApi>();

    /// <summary>An empty folder that exists, to choose as the vault.</summary>
    protected string VaultFolder => _vaultFolder.Path;

    private readonly TempFolder _vaultFolder = new();

    /// <summary>An empty folder that exists, to choose as the movie folder and put video files in.</summary>
    protected string MoviesFolder => _moviesFolder.Path;

    private readonly TempFolder _moviesFolder = new();

    protected IVaultApi VaultApi => Services.GetRequiredService<IVaultApi>();

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
        _vaultFolder.Dispose();
        _moviesFolder.Dispose();
        GC.SuppressFinalize(this);
    }

    protected async Task<MovieDto> SeedAsync(SaveMovieRequest request)
    {
        var response = await Api.AddMovieAsync(request, Ct);
        response.Status.ShouldBe(SaveStatus.Saved, string.Join("; ", response.Errors));
        return response.Movie!;
    }

    protected async Task<ComicDto> SeedComicAsync(ComicDetails details)
    {
        var response = await Api.AddComicAsync(Request(details), Ct);
        response.Status.ShouldBe(SaveStatus.Saved, string.Join("; ", response.Errors));
        return response.Comic!;
    }

    protected static SaveComicRequest Request(ComicDetails details) => new()
    {
        Format = (ComicFormatKind)(int)details.Format,
        Series = details.Series,
        SeriesYear = details.SeriesYear,
        Number = details.Number,
        Title = details.Title,
        Variant = details.Variant,
        Printing = details.Printing,
        Publisher = details.Publisher,
        CoverDate = details.CoverDate,
        Writers = details.Writers,
        Artists = details.Artists,
        Summary = details.Summary,
        Barcode = details.Barcode,
        Isbn = details.Isbn,
        MetronId = details.MetronId,
        MetronSeriesId = details.MetronSeriesId,
        IsKeyIssue = details.IsKeyIssue,
        KeyNote = details.KeyNote,
        CoverSource = details.CoverUrl,
    };

    /// <summary>The current location relative to the app root, e.g. <c>movies/…</c> or <c>?q=heat</c>.</summary>
    protected string RelativeLocation => Navigation.ToBaseRelativePath(Navigation.Uri);
}

/// <summary>Answers the open-a-file dialog with <see cref="Answer"/> (<see langword="null"/>: cancelled).</summary>
public sealed class FakeFilePicker : IVideoFilePicker
{
    public string? Answer { get; set; }

    public Task<string?> PickVideoFileAsync() => Task.FromResult(Answer);
}

/// <summary>Answers the folder dialog with <see cref="Answer"/> (<see langword="null"/>: cancelled).</summary>
public sealed class FakeFolderPicker : IFolderPicker
{
    public string? Answer { get; set; }

    public int Opened { get; private set; }

    public Task<string?> PickFolderAsync()
    {
        Opened++;
        return Task.FromResult(Answer);
    }
}
