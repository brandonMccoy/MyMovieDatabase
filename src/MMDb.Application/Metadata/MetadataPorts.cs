using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Application.Metadata;

/// <summary>Port: an online film database (TMDB).</summary>
public interface IMovieMetadata
{
    /// <summary>TMDB ids of the films matching a title, best match first.</summary>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<IReadOnlyList<int>> SearchAsync(string title, int? year, CancellationToken cancellationToken = default);

    /// <summary>One film's details, ready to edit: TMDB id, trailer and poster path included.</summary>
    /// <returns><see langword="null"/> if the database has no such film.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<MovieDetails?> GetAsync(int tmdbId, CancellationToken cancellationToken = default);
}

/// <summary>Port: a barcode database that names retail products (UPCitemdb).</summary>
public interface IBarcodeLookup
{
    /// <returns>The product names listed for the barcode, e.g. "Heat [Blu-ray]"; empty if it is unknown.</returns>
    /// <exception cref="MetadataException">The database could not be asked.</exception>
    Task<IReadOnlyList<string>> FindProductNamesAsync(Upc upc, CancellationToken cancellationToken = default);
}

/// <summary>Port: downloads poster images.</summary>
public interface IPosterSource
{
    /// <param name="posterPath">See <see cref="MovieDetails.PosterPath"/>.</param>
    /// <returns>The image file's bytes, or <see langword="null"/> if it could not be downloaded.</returns>
    Task<byte[]?> FetchAsync(string posterPath, CancellationToken cancellationToken = default);
}

/// <summary>Port: where downloaded covers are kept (a folder on the PC).</summary>
public interface ICoverStore
{
    /// <param name="name">A file name, no folders. An existing cover with that name is replaced.</param>
    Task SaveAsync(string name, byte[] image, CancellationToken cancellationToken = default);

    /// <summary>Removes the cover if it is there.</summary>
    void Delete(string name);
}

public enum MetadataProblem
{
    /// <summary>No API key has been set up.</summary>
    NotConfigured,

    /// <summary>The service turned the API key down.</summary>
    KeyRejected,

    /// <summary>Too many requests; try again later.</summary>
    Busy,

    /// <summary>Offline, timed out, or the service failed.</summary>
    Unavailable,
}

/// <summary>An online database could not answer.</summary>
public sealed class MetadataException(string service, MetadataProblem problem, Exception? innerException = null)
    : Exception($"{service}: {problem}", innerException)
{
    /// <summary>Which service, as shown to the user: "TMDB", "UPCitemdb".</summary>
    public string Service { get; } = service;

    public MetadataProblem Problem { get; } = problem;
}
