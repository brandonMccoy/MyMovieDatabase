using MMDb.Application.Comics;
using MMDb.Application.Movies;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;

namespace MMDb.Application.Vault;

/// <summary>
/// Everything the vault mirrors: every film (as its index lists them), every comic, and what Metron says about
/// their series.
/// </summary>
public sealed record VaultContent(IReadOnlyList<MovieListing> Movies, IReadOnlyList<Comic> Comics, IReadOnlyList<ComicSeries> Series)
{
    public static readonly VaultContent Empty = new([], [], []);

    /// <summary>Notes Shelf keeps in the vault: one per film and one per comic.</summary>
    public int NoteCount => Movies.Count + Comics.Count;
}

/// <summary>Reads the <see cref="VaultContent"/> from this unit of work's repositories.</summary>
public sealed class VaultContentSource(IMovieRepository movies, IComicRepository comics)
{
    public async Task<VaultContent> LoadAsync(CancellationToken cancellationToken = default) =>
        new(await movies.ListAsync(cancellationToken), await comics.ListAsync(cancellationToken), await comics.ListSeriesAsync(cancellationToken));

    /// <summary>Every film in full, credits and all: what it takes to write every film's note again.</summary>
    public Task<IReadOnlyList<Movie>> LoadFilmsAsync(CancellationToken cancellationToken = default) => movies.ListInFullAsync(cancellationToken);
}
