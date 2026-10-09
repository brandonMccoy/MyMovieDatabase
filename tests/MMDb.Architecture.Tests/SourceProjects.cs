using System.Xml.Linq;

namespace MMDb.Architecture.Tests;

/// <summary>Reads the project files under <c>src/</c>.</summary>
internal static class SourceProjects
{
    private static readonly Lazy<string> SrcDirectory = new(FindSrcDirectory);

    public static IReadOnlyList<string> Names() =>
        [.. Directory.GetDirectories(SrcDirectory.Value)
            .Where(dir => File.Exists(ProjectFile(Path.GetFileName(dir))))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> ProjectReferences(string project) =>
        [.. Items(project, "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))];

    public static IReadOnlyList<string> PackageReferences(string project) =>
        [.. Items(project, "PackageReference")];

    private static IEnumerable<string> Items(string project, string itemType) =>
        XDocument.Load(ProjectFile(project))
            .Descendants(itemType)
            .Select(item => (string?)item.Attribute("Include"))
            .OfType<string>();

    private static string ProjectFile(string project) =>
        Path.Combine(SrcDirectory.Value, project, project + ".csproj");

    private static string FindSrcDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MyMovieDatabase.slnx")))
            {
                return Path.Combine(dir.FullName, "src");
            }
        }

        throw new InvalidOperationException("Could not find MyMovieDatabase.slnx above " + AppContext.BaseDirectory);
    }
}
