using MMDb.Application.Vault;
using MMDb.Domain.Movies;

namespace MMDb.Testing;

/// <summary>Records what the vault was asked to write, or fails every write while <see cref="FailWith"/> is set.</summary>
internal sealed class FakeVault : IVaultWriter
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly Dictionary<Guid, string> _notes = [];

    public VaultProblem? FailWith { get; set; }

    /// <summary>"sync C:\vault (2 films)", "write Heat", "remove Heat".</summary>
    public IReadOnlyList<string> Calls { get { lock (_lock) { return [.. _calls]; } } }

    public static string NoteFor(Movie movie) => $"Movies/{movie.Title} ({movie.Year}).md";

    public Task SyncAsync(string folder, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        Record($"sync {folder} ({movies.Count} films)", () =>
        {
            _notes.Clear();
            foreach (var movie in movies)
            {
                _notes[movie.Id] = NoteFor(movie);
            }
        });

    public Task WriteMovieAsync(string folder, Movie movie, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        Record($"write {movie.Title}", () => _notes[movie.Id] = NoteFor(movie));

    public Task RemoveMovieAsync(string folder, Guid movieId, IReadOnlyList<Movie> movies, CancellationToken cancellationToken = default) =>
        Record($"remove {_notes.GetValueOrDefault(movieId, movieId.ToString())}", () => _notes.Remove(movieId));

    public string? NoteOf(string folder, Guid movieId)
    {
        lock (_lock)
        {
            return _notes.GetValueOrDefault(movieId);
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
