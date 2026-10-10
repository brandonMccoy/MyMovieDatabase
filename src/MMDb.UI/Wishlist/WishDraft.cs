using MMDb.Contracts.Movies;

namespace MMDb.UI.Wishlist;

/// <summary>A film about to go on the wishlist: what the wishlist keeps of it, before the format is chosen.</summary>
/// <param name="PosterUrl">The poster to show meanwhile (needs internet).</param>
public sealed record WishDraft(
    string Title,
    int? Year,
    IReadOnlyList<string> Directors,
    int? TmdbId = null,
    string? TrailerYouTubeKey = null,
    string? PosterPath = null,
    Uri? PosterUrl = null)
{
    public static WishDraft From(MovieCandidateDto candidate) => new(
        candidate.Title, candidate.Year, candidate.Directors, candidate.TmdbId, candidate.TrailerYouTubeKey, candidate.PosterPath, candidate.PosterUrl);
}
