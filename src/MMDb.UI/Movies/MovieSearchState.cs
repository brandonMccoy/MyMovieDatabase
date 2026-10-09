using MMDb.Contracts.Movies;

namespace MMDb.UI.Movies;

/// <summary>What was searched for and found, kept while the user reviews a match.</summary>
public sealed class MovieSearchState
{
    public string Query { get; set; } = "";

    /// <summary>The query <see cref="Response"/> answers.</summary>
    public string? SearchedFor { get; set; }

    public MovieLookupResponse? Response { get; set; }
}
