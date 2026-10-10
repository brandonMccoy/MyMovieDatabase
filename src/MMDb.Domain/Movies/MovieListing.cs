namespace MMDb.Domain.Movies;

/// <summary>
/// A film as lists show it: the library grid, its filters and sort orders, and the vault's index. It leaves out
/// what only the film's own page needs (cast, crew, overview, trailer), so listing a large collection stays quick.
/// </summary>
/// <param name="Formats">Owned formats in display order (4K UHD, Blu-ray, DVD, Digital).</param>
/// <param name="LastAddedAt">When a format was last added: what "Recently added" sorts by.</param>
public sealed record MovieListing(
    Guid Id,
    string Title,
    int? Year,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaFormat> Formats,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAddedAt,
    int? TmdbId = null,
    string? CoverImage = null)
{
    public bool Owns(MediaFormat format) => Formats.Contains(format);
}
