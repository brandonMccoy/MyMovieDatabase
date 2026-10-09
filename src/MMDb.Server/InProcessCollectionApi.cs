using MMDb.Application.Metadata;
using MMDb.Application.Movies;
using MMDb.Contracts;
using MMDb.Contracts.Movies;
using MMDb.Server.Movies;

namespace MMDb.Server;

/// <summary>
/// <see cref="ICollectionApi"/> for the PC app: calls the use cases directly, no HTTP.
/// Each call gets its own DI scope (and so its own database unit of work), like a web request.
/// </summary>
public sealed class InProcessCollectionApi(IServiceScopeFactory scopes, CoverLinks covers) : ICollectionApi
{
    public Task<MovieListResponse> ListMoviesAsync(MovieListRequest request, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, MovieListResponse>(async catalog => (await catalog.ListAsync(request.ToDomain(), cancellationToken)).ToContract(covers));

    public Task<MovieDto?> GetMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, MovieDto?>(async catalog => (await catalog.GetAsync(id, cancellationToken))?.ToDto(covers));

    public Task<SaveMovieResponse> AddMovieAsync(SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, SaveMovieResponse>(async catalog => (await catalog.AddAsync(request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract(covers));

    public Task<SaveMovieResponse> UpdateMovieAsync(Guid id, int version, SaveMovieRequest request, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, SaveMovieResponse>(async catalog => (await catalog.UpdateAsync(id, version, request.ToDetails(), request.ToFormats(), cancellationToken)).ToContract(covers));

    public Task<bool> DeleteMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        With<MovieCatalog, bool>(catalog => catalog.DeleteAsync(id, cancellationToken));

    public Task<MovieLookupResponse> LookupMovieAsync(string query, CancellationToken cancellationToken = default) =>
        With<MovieLookup, MovieLookupResponse>(async lookup => (await lookup.LookupAsync(query, cancellationToken)).ToContract(covers));

    private async Task<TResult> With<TService, TResult>(Func<TService, Task<TResult>> use)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
