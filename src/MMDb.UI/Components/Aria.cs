namespace MMDb.UI.Components;

public static class Aria
{
    /// <summary>
    /// "true"/"false" for aria-pressed and friends. A plain bool would render as a bare attribute
    /// (or none), which assistive tech doesn't read as a state.
    /// </summary>
    public static string Bool(bool value) => value ? "true" : "false";
}
