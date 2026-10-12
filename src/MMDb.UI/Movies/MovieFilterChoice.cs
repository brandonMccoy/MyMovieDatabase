using MMDb.Contracts.Movies;

namespace MMDb.UI.Movies;

/// <summary>What is ticked in the Movies filter panel. Nothing ticked for a type means any of it.</summary>
public sealed record MovieFilterChoice(IReadOnlyList<MovieFormatKind> Formats, IReadOnlyList<string> Genres, IReadOnlyList<int> Decades)
{
    public static MovieFilterChoice None { get; } = new([], [], []);
}
