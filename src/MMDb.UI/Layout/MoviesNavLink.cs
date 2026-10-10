using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MMDb.UI.Layout;

/// <summary>The sidebar's Movies link: lit on every page except Settings (all the others are movie pages so far).</summary>
public sealed class MoviesNavLink : NavLink
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute) =>
        !Navigation.ToBaseRelativePath(uriAbsolute).StartsWith("settings", StringComparison.OrdinalIgnoreCase);
}
