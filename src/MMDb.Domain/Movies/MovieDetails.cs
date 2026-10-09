using System.Text.RegularExpressions;

namespace MMDb.Domain.Movies;

/// <summary>The descriptive, user-editable fields of a film.</summary>
public sealed partial record MovieDetails
{
    public const int MinYear = 1888;
    public const int MaxYear = 2100;
    public const int MaxRuntimeMinutes = 1440;
    public const int MaxTitleLength = 300;
    public const int MaxRatingLength = 16;

    public required string Title { get; init; }

    public int? Year { get; init; }

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<CastMember> Cast { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public int? RuntimeMinutes { get; init; }

    /// <summary>Age certification as printed on the case, e.g. "R" or "PG-13".</summary>
    public string? Rating { get; init; }

    public string? Overview { get; init; }

    public int? TmdbId { get; init; }

    public string? TrailerYouTubeKey { get; init; }

    /// <summary>TMDB's name for the poster image, e.g. <c>/rrBuGu0Pjq7Y2BWSI6teGfZzviY.jpg</c>.</summary>
    public string? PosterPath { get; init; }

    /// <summary>Trims text, turns blanks into nulls, drops empty and duplicate list entries.</summary>
    public MovieDetails Normalized() => this with
    {
        Title = Title?.Trim() ?? "",
        Directors = DistinctNonBlank(Directors),
        Genres = DistinctNonBlank(Genres),
        Cast = [.. Cast
            .Select(c => new CastMember(c.Name?.Trim() ?? "", NullIfBlank(c.Character)))
            .Where(c => c.Name.Length > 0 || c.Character is not null)],
        Rating = NullIfBlank(Rating),
        Overview = NullIfBlank(Overview),
        TrailerYouTubeKey = NullIfBlank(TrailerYouTubeKey),
        PosterPath = NullIfBlank(PosterPath),
    };

    /// <summary>Validates the normalized form of these details.</summary>
    public IReadOnlyList<ValidationError> Validate()
    {
        var details = Normalized();
        var errors = new List<ValidationError>();

        if (details.Title.Length == 0)
        {
            errors.Add(new(MovieFields.Title, "Title is required."));
        }
        else if (details.Title.Length > MaxTitleLength)
        {
            errors.Add(new(MovieFields.Title, $"Title can be at most {MaxTitleLength} characters."));
        }

        if (details.Year is < MinYear or > MaxYear)
        {
            errors.Add(new(MovieFields.Year, $"Year must be between {MinYear} and {MaxYear}."));
        }

        if (details.RuntimeMinutes is < 1 or > MaxRuntimeMinutes)
        {
            errors.Add(new(MovieFields.RuntimeMinutes, $"Runtime must be between 1 and {MaxRuntimeMinutes} minutes."));
        }

        if (details.Rating?.Length > MaxRatingLength)
        {
            errors.Add(new(MovieFields.Rating, $"Rating can be at most {MaxRatingLength} characters."));
        }

        if (details.Cast.Any(c => c.Name.Length == 0))
        {
            errors.Add(new(MovieFields.Cast, "Every cast member needs a name."));
        }

        // It ends up in a file name and a URL, so only TMDB's own shape is accepted.
        if (details.PosterPath is { } poster && !PosterPathShape().IsMatch(poster))
        {
            errors.Add(new(MovieFields.PosterPath, "Not a TMDB poster path."));
        }

        return errors;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] DistinctNonBlank(IEnumerable<string> values) =>
        [.. values
            .Select(v => v?.Trim())
            .OfType<string>()
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    [GeneratedRegex(@"^/[A-Za-z0-9_-]{1,100}\.(?:jpg|jpeg|png|webp)$")]
    private static partial Regex PosterPathShape();
}
