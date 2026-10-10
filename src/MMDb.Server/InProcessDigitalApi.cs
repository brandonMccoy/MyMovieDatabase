using System.Runtime.CompilerServices;
using MMDb.Application.Digital;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Digital;
using MMDb.Contracts.Movies;
using MMDb.Infrastructure.Metadata;
using MMDb.Server.Movies;

namespace MMDb.Server;

/// <summary>
/// <see cref="IDigitalApi"/> for the PC app: calls the import use cases directly, one DI scope per call and off
/// the UI thread, and scans the movie folder again whenever its watcher says files changed.
/// </summary>
public sealed class InProcessDigitalApi : IDigitalApi, IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ImportDesk _desk;
    private readonly InProcessCollectionApi _collection;
    private readonly CoverLinks _covers;

    // A file's row is mapped once: looking up a thousand files changes the status a thousand times, one file each.
    private readonly ConditionalWeakTable<ImportFile, ImportFileDto> _files = [];
    private Mapped? _mapped;

    public InProcessDigitalApi(IServiceScopeFactory scopes, ImportDesk desk, InProcessCollectionApi collection, CoverLinks covers)
    {
        _scopes = scopes;
        _desk = desk;
        _collection = collection;
        _covers = covers;
        desk.FolderChanged += OnFolderChanged;
    }

    public event Action? Changed
    {
        add => _desk.Changed += value;
        remove => _desk.Changed -= value;
    }

    public MovieFolderDto Folder
    {
        get
        {
            // Mapped once per change: the sidebar, Settings and the import screen all read it on every render.
            var status = _desk.Status;
            var mapped = _mapped;
            if (mapped is null || !ReferenceEquals(mapped.Status, status))
            {
                _mapped = mapped = new(status, ToDto(status));
            }

            return mapped.Dto;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Run(import => import.StartAsync(cancellationToken));

    public Task<bool> SetFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        With(import => import.SetFolderAsync(folder, cancellationToken));

    public Task SetWatchAsync(bool watch, CancellationToken cancellationToken = default) =>
        Run(import => import.SetWatchAsync(watch, cancellationToken));

    public Task RescanAsync(CancellationToken cancellationToken = default) =>
        Run(import => import.RescanAsync(cancellationToken));

    public Task MatchAsync(CancellationToken cancellationToken = default) =>
        Run(import => import.MatchAsync(cancellationToken));

    public Task<FilmSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        With(async import =>
        {
            var found = await import.SearchAsync(query, cancellationToken);
            return new FilmSearchResponse(
                found.Problem is { } problem ? ToStatus(problem) : found.Hits.Count > 0 ? LookupStatus.Found : LookupStatus.NoMatches,
                [.. found.Hits.Select(ToDto)],
                found.FailedService);
        });

    public Task ChooseAsync(string path, FilmHitDto film, CancellationToken cancellationToken = default) =>
        Run(import => import.ChooseAsync(path, new MovieHit(film.TmdbId, film.Title, film.Year, film.PosterPath), cancellationToken));

    public void Tick(string path, bool ticked)
    {
        using var scope = _scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<DigitalImport>().Tick(path, ticked);
    }

    public Task SkipAsync(string path, CancellationToken cancellationToken = default) =>
        Run(import => import.SkipAsync(path, cancellationToken));

    public Task BringBackAsync(string path, CancellationToken cancellationToken = default) =>
        Run(import => import.BringBackAsync(path, cancellationToken));

    public Task<int> AddTickedAsync(CancellationToken cancellationToken = default) =>
        Saved(With(import => import.AddTickedAsync(cancellationToken)), added => added > 0);

    public Task<bool> UseInsteadAsync(string path, CancellationToken cancellationToken = default) =>
        Saved(With(import => import.UseInsteadAsync(path, cancellationToken)), used => used);

    public Task<bool> LocateAsync(Guid movieId, string path, CancellationToken cancellationToken = default) =>
        Saved(With(import => import.LocateAsync(movieId, path, cancellationToken)), located => located);

    public Task<bool> RemoveCopyAsync(Guid movieId, CancellationToken cancellationToken = default) =>
        Saved(With(import => import.RemoveCopyAsync(movieId, cancellationToken)), removed => removed);

    public Task<bool> PlayAsync(Guid movieId, CancellationToken cancellationToken = default) =>
        With(import => import.PlayAsync(movieId, cancellationToken));

    public bool FileExists(string path) => File.Exists(path);

    public void Dispose() => _desk.FolderChanged -= OnFolderChanged;

    private void OnFolderChanged() => _ = RescanAsync();

    // Open screens (the library, the sidebar's counts) hear of films the import saved like of any other save.
    private async Task<TResult> Saved<TResult>(Task<TResult> saving, Func<TResult, bool> changed)
    {
        var result = await saving;
        if (changed(result))
        {
            _collection.RaiseChanged();
        }

        return result;
    }

    private MovieFolderDto ToDto(ImportStatus status) => new()
    {
        State = status.State switch
        {
            ImportFolderState.NotSetUp => MovieFolderState.NotSetUp,
            ImportFolderState.Ready => MovieFolderState.Ready,
            _ => MovieFolderState.FolderMissing,
        },
        Folder = status.Folder,
        Watch = status.Watch,
        VideoFileCount = status.VideoFileCount,
        LinkedCount = status.LinkedCount,
        Files = [.. status.Files.Select(file => _files.GetValue(file, ToDto))],
        Skipped = [.. status.Skipped.Select(path => new SkippedFileDto(path, Name(status.Folder, path)))],
        Missing = [.. status.Missing.Select(m => new MissingFileDto(m.MovieId, m.Title, m.Year, m.Path, m.OnlyCopy, _covers.For(m.CoverImage)))],
        Progress = status.Progress is { } progress
            ? new(progress.Activity == ImportActivity.Matching ? ImportStep.Matching : ImportStep.Adding, progress.Done, progress.Total)
            : null,
        Problem = status.Problem is { } problem ? ToStatus(problem) : null,
        FailedService = status.FailedService,
    };

    private ImportFileDto ToDto(ImportFile file) => new()
    {
        Path = file.Path,
        Name = file.Name,
        Reading = file.Parsed.Text,
        Match = file.State switch
        {
            FileMatchState.Waiting => FileMatch.Waiting,
            FileMatchState.Matched => FileMatch.Matched,
            FileMatchState.Check => FileMatch.Check,
            _ => FileMatch.NoMatch,
        },
        Film = file.Chosen is { } film ? ToDto(film) : null,
        Hits = [.. file.Hits.Select(ToDto)],
        Ticked = file.Ticked,
        Owned = file.Owned?.ToSummary(_covers),
        OwnedFile = file.OwnedFile,
        SameFilmAs = file.SameFilmAs,
    };

    private static FilmHitDto ToDto(MovieHit hit) => new(
        hit.TmdbId, hit.Title, hit.Year, hit.PosterPath is { } poster ? TmdbImages.Url(poster, TmdbImages.ThumbnailSize) : null, hit.PosterPath);

    private static LookupStatus ToStatus(MetadataProblem problem) => problem switch
    {
        MetadataProblem.NotConfigured => LookupStatus.NotConfigured,
        MetadataProblem.KeyRejected => LookupStatus.KeyRejected,
        MetadataProblem.Busy => LookupStatus.Busy,
        _ => LookupStatus.Unavailable,
    };

    private static string Name(string? folder, string path) =>
        folder is not null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            ? path[folder.Length..].TrimStart(Path.DirectorySeparatorChar)
            : path;

    private Task<bool> Run(Func<DigitalImport, Task> use) =>
        With(async import =>
        {
            await use(import);
            return true;
        });

    // Off the UI thread: reading a folder of a few thousand files, or adding them, takes a while.
    private Task<TResult> With<TResult>(Func<DigitalImport, Task<TResult>> use) =>
        Task.Run(async () =>
        {
            await using var scope = _scopes.CreateAsyncScope();
            return await use(scope.ServiceProvider.GetRequiredService<DigitalImport>());
        });

    private sealed record Mapped(ImportStatus Status, MovieFolderDto Dto);
}
