using AngleSharp.Dom;
using Bunit;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>Reads and works the Movies filter row and its panel, the same on the PC and the phone.</summary>
internal static class FilterPanel
{
    /// <summary>The filter row: the Filter button, then a chip per filter applied.</summary>
    public static string[] Chips(IRenderedComponent<MovieLibrary> page) => [.. page.FindAll(".filters .chip").Select(c => c.TextContent.Trim())];

    /// <summary>Each type of filter in the panel with its tick boxes, a format's count after its name: ("Format", "4K UHD 1, DVD 0").</summary>
    public static (string Type, string Boxes)[] Boxes(IRenderedComponent<MovieLibrary> page) =>
        [.. page.FindAll("[role=dialog] fieldset").Select(f => (
            f.QuerySelector("legend")!.TextContent,
            string.Join(", ", f.QuerySelectorAll("label.check").Select(l => l.QuerySelector(".n") is { } count ? $"{Name(l)} {count.TextContent}" : Name(l)))))];

    /// <summary>The names of the tick boxes that are ticked.</summary>
    public static string[] Ticked(IRenderedComponent<MovieLibrary> page) =>
        [.. page.FindAll("[role=dialog] label.check").Where(l => l.QuerySelector("input")!.HasAttribute("checked")).Select(Name)];

    public static void Tick(IRenderedComponent<MovieLibrary> page, string name, bool on = true) =>
        page.FindAll("[role=dialog] label.check").Single(l => Name(l) == name).QuerySelector("input")!.Change(on);

    public static void Apply(IRenderedComponent<MovieLibrary> page) => page.Find("[role=dialog] .sheet-foot .btn.primary").Click();

    public static void Clear(IRenderedComponent<MovieLibrary> page) => page.Find("[role=dialog] .sheet-foot .btn:not(.primary)").Click();

    private static string Name(IElement label) =>
        string.Concat(label.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent)).Trim();
}
