using MMDb.Contracts.Movies;

namespace MMDb.UI.Movies;

/// <summary>What was searched for and found, kept while the user reviews a match.</summary>
public sealed class MovieSearchState
{
    public string Query { get; set; } = "";

    /// <summary>The query <see cref="Response"/> answers.</summary>
    public string? SearchedFor { get; set; }

    public MovieLookupResponse? Response { get; set; }

    /// <summary>
    /// The UPC last looked up and the format its product names. It is kept while searching on by title, so the
    /// match picked gets the UPC on that format. A UPC whose format isn't known is not kept.
    /// </summary>
    public (string Upc, MovieFormatKind Format)? Barcode { get; private set; }

    /// <summary>Takes the answer to a search. After a UPC, searching on is by the title its product names, if any.</summary>
    public void Answer(string searchedFor, MovieLookupResponse response)
    {
        SearchedFor = searchedFor;
        Response = response;
        if (response.Upc is { } upc)
        {
            Barcode = response.Format is { } format ? (upc, format) : null;
            Query = response.ProductTitle ?? "";
        }
    }
}
