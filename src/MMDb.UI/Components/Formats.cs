using MMDb.Contracts.Movies;

namespace MMDb.UI.Components;

/// <summary>Labels and badge colors for the four formats (docs/design/DECISIONS.md).</summary>
public static class Formats
{
    public static IReadOnlyList<MovieFormatKind> All { get; } = Enum.GetValues<MovieFormatKind>();

    public static string Name(MovieFormatKind format) => format switch
    {
        MovieFormatKind.Uhd4K => "4K UHD",
        MovieFormatKind.BluRay => "Blu-ray",
        MovieFormatKind.Dvd => "DVD",
        MovieFormatKind.Digital => "Digital",
        _ => format.ToString(),
    };

    public static string ShortName(MovieFormatKind format) => format == MovieFormatKind.Uhd4K ? "4K" : Name(format);

    public static string CssClass(MovieFormatKind format) => format switch
    {
        MovieFormatKind.Uhd4K => "uhd",
        MovieFormatKind.BluRay => "br",
        MovieFormatKind.Dvd => "dvd",
        MovieFormatKind.Digital => "dig",
        _ => "",
    };

    public static bool IsDisc(MovieFormatKind format) => format != MovieFormatKind.Digital;
}
