using MMDb.Application.Movies;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Server.Movies;

namespace MMDb.Server;

/// <summary>
/// <see cref="ICollectionApi"/> for the PC app: calls the use cases directly, no HTTP.
/// Each call gets its own DI scope (and so its own database unit of work), like a web request.
/// </summary>
public sealed class InProcessCollectionApi(IServiceScopeFactory scopes) : ICollectionApi
{
    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default) =>
        WithMovies(async catalog => (await catalog.ListAsync(request.ToDomain(), cancellationToken)).ToContract());

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithMovies(async catalog => (await catalog.GetAsync(id, cancellationToken))?.ToDto());

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        WithMovies(async catalog => (await catalog.AddAsync(request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract());

    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        WithMovies(async catalog => (await catalog.UpdateAsync(id, version, request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract());

    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithMovies(catalog => catalog.DeleteAsync(id, cancellationToken));

    private async Task<T> WithMovies<T>(Func<MovieCatalog, Task<T>> use)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<MovieCatalog>());
    }
}
