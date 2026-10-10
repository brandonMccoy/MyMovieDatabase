namespace MMDb.Domain.Movies;

/// <summary>Field keys used in <see cref="ValidationError.Field"/> for movie input.</summary>
public static class MovieFields
{
    public const string Title = nameof(Title);
    public const string Year = nameof(Year);
    public const string RuntimeMinutes = nameof(RuntimeMinutes);
    public const string Rating = nameof(Rating);
    public const string Cast = nameof(Cast);
    public const string Crew = nameof(Crew);
    public const string Formats = nameof(Formats);
    public const string PosterPath = nameof(PosterPath);

    /// <summary>e.g. <c>BluRay.Upc</c>.</summary>
    public static string Upc(MediaFormat format) => $"{format}.Upc";

    /// <summary>e.g. <c>Digital.FilePath</c>.</summary>
    public static string FilePath(MediaFormat format) => $"{format}.FilePath";
}
