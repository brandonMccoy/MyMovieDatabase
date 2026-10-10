using MMDb.Application.Vault;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Testing;

/// <summary>Records what the vault was asked to write, or fails every write while <see cref="FailWith"/> is set.</summary>
internal sealed class FakeVault : IVaultWriter
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly Dictionary<Guid, string> _notes = [];

    public VaultProblem? FailWith { get; set; }

    /// <summary>"sync C:\vault (2 films)", "sync C:\vault (2 films, 1 comics)", "write Heat", "write Saga #7", "remove Movies/Heat (1995).md".</summary>
    public IReadOnlyList<string> Calls { get { lock (_lock) { return [.. _calls]; } } }

    public static string NoteFor(Movie movie) => $"Movies/{movie.Title} ({movie.Year}).md";

    /// <summary>The films the last sync was given in full, with how many credits each came with.</summary>
    public IReadOnlyList<(string Title, int Credits)> SyncedFilms { get; private set; } = [];

    public static string NoteFor(Comic comic) => $"Comics/{comic.Series} ({comic.SeriesYear})/{comic.Series} {comic.Number}.md";

    public Task SyncAsync(string folder, VaultContent content, IReadOnlyList<Movie> films, CancellationToken cancellationToken = default) =>
        Record($"sync {folder} ({content.Movies.Count} films{(content.Comics.Count > 0 ? $", {content.Comics.Count} comics" : "")})", () =>
        {
            SyncedFilms = [.. films.Select(f => (f.Title, f.Cast.Count + f.Crew.Count))];
            _notes.Clear();
            foreach (var movie in films)
            {
                _notes[movie.Id] = NoteFor(movie);
            }

            foreach (var comic in content.Comics)
            {
                _notes[comic.Id] = NoteFor(comic);
            }
        });

    public Task WriteMovieAsync(string folder, Movie movie, VaultContent content, CancellationToken cancellationToken = default) =>
        Record($"write {movie.Title}", () => _notes[movie.Id] = NoteFor(movie));

    public Task WriteComicAsync(string folder, Comic comic, VaultContent content, CancellationToken cancellationToken = default) =>
        Record($"write {comic.DisplayName}", () => _notes[comic.Id] = NoteFor(comic));

    public Task RemoveAsync(string folder, Guid id, VaultContent content, CancellationToken cancellationToken = default) =>
        Record($"remove {_notes.GetValueOrDefault(id, id.ToString())}", () => _notes.Remove(id));

    /// <summary>Recorded as "wishlist (2 wanted)".</summary>
    public Task WriteWishlistAsync(string folder, VaultContent content, CancellationToken cancellationToken = default) =>
        Record($"wishlist ({content.WantedMovies.Count + content.WantedComics.Count} wanted)", () => { });

    public string? NoteOf(string folder, Guid id)
    {
        lock (_lock)
        {
            return _notes.GetValueOrDefault(id);
        }
    }

    private Task Record(string call, Action change)
    {
        lock (_lock)
        {
            if (FailWith is { } problem)
            {
                throw new VaultUnavailableException(problem, problem == VaultProblem.FolderMissing ? "Folder not found" : "Access denied");
            }

            _calls.Add(call);
            change();
        }

        return Task.CompletedTask;
    }
}
