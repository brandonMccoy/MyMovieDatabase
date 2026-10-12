using MMDb.Contracts.Scans;
using MMDb.Contracts.Types;

namespace MMDb.UI;

/// <summary>Which screens belong to which type of collection, so that a type not in use has none to open.</summary>
public static class TypeRoutes
{
    /// <summary>The first screen: Movies, or Comics when movies aren't in use.</summary>
    public static string Home(TypesInUseDto types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return types.Movies ? "" : "comics";
    }

    /// <summary>The library of the first type in use, as the screens that belong to no type name it.</summary>
    public static string HomeName(TypesInUseDto types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return types.Movies ? "Movies" : "Comics";
    }

    /// <returns>The type the screen at this address belongs to; <see langword="null"/> for a screen that belongs to none (or to all).</returns>
    /// <param name="address">Relative to the app root, with or without a query: <c>movies/new</c>, <c>comics?shelf=Issues</c>.</param>
    public static ProductKind? KindOf(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var end = address.AsSpan().IndexOfAny('?', '#');
        var path = (end < 0 ? address : address[..end]).Trim('/');
        return path.Length == 0 || Under(path, "movies") || Under(path, "import") || Under(path, "wishlist/movies") ? ProductKind.Movie
            : Under(path, "comics") || Under(path, "wishlist/comics") ? ProductKind.Comic
            : null;
    }

    /// <returns>Where to go instead, if the screen at this address belongs to a type that isn't in use.</returns>
    public static string? Instead(string address, TypesInUseDto types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return KindOf(address) is { } kind && !types.Uses(kind) ? Home(types) : null;
    }

    private static bool Under(string path, string section) =>
        path.Equals(section, StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(section, StringComparison.OrdinalIgnoreCase) && path[section.Length] == '/');
}
