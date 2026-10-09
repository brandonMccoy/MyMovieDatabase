namespace MMDb.Domain;

/// <summary>How a film is owned. Declared in display order (badges, filter chips).</summary>
public enum MediaFormat
{
    Uhd4K,
    BluRay,
    Dvd,
    Digital,
}

public static class MediaFormatExtensions
{
    public static string DisplayName(this MediaFormat format) => format switch
    {
        MediaFormat.Uhd4K => "4K UHD",
        MediaFormat.BluRay => "Blu-ray",
        MediaFormat.Dvd => "DVD",
        MediaFormat.Digital => "Digital",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>Discs are the physical formats; Digital is a file.</summary>
    public static bool IsDisc(this MediaFormat format) => format != MediaFormat.Digital;
}
