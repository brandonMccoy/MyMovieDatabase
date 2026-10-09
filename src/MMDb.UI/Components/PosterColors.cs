namespace MMDb.UI.Components;

/// <summary>Picks a placeholder poster gradient for a title. The same title always gets the same colors.</summary>
public static class PosterColors
{
    // The placeholder pairs from docs/design/mockup-v1.html.
    private static readonly (string Top, string Bottom)[] Pairs =
    [
        ("#1f4e79", "#0b1824"), ("#c4621d", "#2a0f05"), ("#2f5d73", "#060d12"), ("#6b7280", "#1a1d22"),
        ("#1f2a1f", "#030503"), ("#d97a1f", "#3a1404"), ("#3f5e3a", "#101a0e"), ("#a8834f", "#2b1e0e"),
        ("#24313f", "#05080c"), ("#b8763a", "#2d1608"), ("#3d7a8c", "#10262d"), ("#1d5f8a", "#071a28"),
        ("#3b4f63", "#0c1219"), ("#8a5a3a", "#1e1008"), ("#4a5560", "#101418"), ("#a2471f", "#1c0904"),
        ("#7a3d8c", "#1c0d24"), ("#3a2a1d", "#0a0705"),
    ];

    public static (string Top, string Bottom) For(string? title)
    {
        // FNV-1a: stable across runs, unlike string.GetHashCode.
        var hash = 2166136261u;
        foreach (var c in (title ?? "").Trim().ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return Pairs[hash % (uint)Pairs.Length];
    }

    public static string Style(string? title)
    {
        var (top, bottom) = For(title);
        return $"--p1:{top};--p2:{bottom}";
    }
}
