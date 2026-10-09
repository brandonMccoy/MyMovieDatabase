namespace MMDb.Domain.Movies;

/// <summary>One format a film is owned on. A film has at most one of each format.</summary>
public sealed class MovieFormat
{
    // For EF Core.
    private MovieFormat()
    {
    }

    internal MovieFormat(FormatDetails details, DateTimeOffset addedAt)
    {
        Format = details.Format;
        AddedAt = addedAt;
        Apply(details);
    }

    public MediaFormat Format { get; private init; }

    public Upc? Upc { get; private set; }

    /// <summary>Digital only: where the video file is on the PC.</summary>
    public string? FilePath { get; private set; }

    public DateTimeOffset AddedAt { get; private init; }

    internal void Apply(FormatDetails details)
    {
        Upc = details.ParsedUpc;
        FilePath = details.NormalizedFilePath;
    }
}
