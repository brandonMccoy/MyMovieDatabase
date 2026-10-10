using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MMDb.UI.Layout;

/// <summary>The sidebar's Movies link: lit on the library (<c>/</c>) and every movie page, but not on Comics or Settings.</summary>
public sealed class MoviesNavLink : NavLink
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute) =>
        Navigation.ToBaseRelativePath(uriAbsolute) is var path
        && !path.StartsWith("settings", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("comics", StringComparison.OrdinalIgnoreCase);
}
