namespace MMDb.Architecture.Tests;

/// <summary>
/// Dependency rule: Domain ← Application ← Infrastructure/Server; UI depends only on Contracts.
/// MMDb.App is the composition root and may reference anything.
/// </summary>
public sealed class LayerDependencyTests
{
    private const string CompositionRoot = "MMDb.App";

    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["MMDb.Domain"] = [],
        ["MMDb.Contracts"] = [],
        ["MMDb.Application"] = ["MMDb.Domain"],
        ["MMDb.Infrastructure"] = ["MMDb.Application", "MMDb.Domain"],
        ["MMDb.Local"] = ["MMDb.Application", "MMDb.Infrastructure", "MMDb.Contracts", "MMDb.Domain"],
        ["MMDb.Server"] = ["MMDb.Local", "MMDb.Application", "MMDb.Infrastructure", "MMDb.Contracts", "MMDb.Domain"],
        ["MMDb.Client"] = ["MMDb.Local", "MMDb.Application", "MMDb.Infrastructure", "MMDb.Contracts", "MMDb.Domain"],
        ["MMDb.UI"] = ["MMDb.Contracts"],
    };

    public static TheoryData<string> RuledProjects => [.. AllowedReferences.Keys];

    [Theory]
    [MemberData(nameof(RuledProjects))]
    public void Project_references_only_the_layers_it_may_depend_on(string project)
    {
        var forbidden = SourceProjects.ProjectReferences(project).Except(AllowedReferences[project]);

        forbidden.ShouldBeEmpty($"{project} may only reference: {string.Join(", ", AllowedReferences[project])}");
    }

    [Fact]
    public void Every_source_project_has_a_dependency_rule()
    {
        var unruled = SourceProjects.Names().Except(AllowedReferences.Keys).Except([CompositionRoot]);

        unruled.ShouldBeEmpty("add new src projects to AllowedReferences");
    }

    [Fact]
    public void Domain_has_no_package_dependencies()
    {
        SourceProjects.PackageReferences("MMDb.Domain").ShouldBeEmpty();
    }
}
