using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MMDb.UI.Layout;

/// <summary>
/// The sidebar's Movies link: lit on the library (<c>/</c>, whatever its search and filters), every movie page
/// and the digital import, and on no other screen.
/// </summary>
public sealed class MoviesNavLink : NavLink
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute)
    {
        var path = Navigation.ToBaseRelativePath(uriAbsolute);
        var end = path.AsSpan().IndexOfAny('?', '#');
        path = (end < 0 ? path : path[..end]).TrimEnd('/');
        return path.Length == 0
            || path.Equals("movies", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("movies/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("import", StringComparison.OrdinalIgnoreCase);
    }
}
