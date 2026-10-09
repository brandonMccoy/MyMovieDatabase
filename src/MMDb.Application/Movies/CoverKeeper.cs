using MMDb.Application.Metadata;
using MMDb.Domain.Movies;

namespace MMDb.Application.Movies;

/// <summary>Keeps a film's stored cover in step with its poster path, around the save that changes it.</summary>
internal sealed class CoverKeeper(IPosterSource posters, ICoverStore store)
{
    /// <summary>One file per film and poster, so a new poster never overwrites the one in use.</summary>
    public static string CoverName(Guid movieId, string posterPath) => $"{movieId:N}-{posterPath.TrimStart('/')}";

    /// <summary>
    /// Downloads the poster if the film needs a new cover, runs <paramref name="save"/>, then removes the
    /// cover it replaced. If the download fails (offline) the old cover stays and the next save tries again.
    /// If the save fails, the new file is removed and the error passes through.
    /// </summary>
    public async Task SaveAsync(Movie movie, Func<Task> save, CancellationToken cancellationToken)
    {
        var old = movie.CoverImage;
        var wanted = movie.PosterPath is { } path ? CoverName(movie.Id, path) : null;
        string? written = null;

        if (wanted is null)
        {
            movie.UseCover(null);
        }
        else if (wanted != old && await posters.FetchAsync(movie.PosterPath!, cancellationToken) is { } image)
        {
            await store.SaveAsync(wanted, image, cancellationToken);
            written = wanted;
            movie.UseCover(wanted);
        }

        try
        {
            await save();
        }
        catch
        {
            movie.UseCover(old);
            if (written is not null)
            {
                store.Delete(written);
            }

            throw;
        }

        if (old is not null && old != movie.CoverImage)
        {
            store.Delete(old);
        }
    }

    public void Forget(Movie movie)
    {
        if (movie.CoverImage is { } cover)
        {
            store.Delete(cover);
        }
    }
}
