using MMDb.Domain.Comics;

namespace MMDb.Testing;

/// <summary>Sample comics with sensible defaults; tweak per test with <c>with { … }</c>.</summary>
internal static class TestComics
{
    /// <summary>Saga #7 as Metron has it (issue 4521 in series 1234).</summary>
    public static ComicDetails Saga7 => new()
    {
        Series = "Saga",
        SeriesYear = 2012,
        Number = "7",
        Title = "Chapter Seven",
        Publisher = "Image",
        CoverDate = new DateOnly(2012, 11, 1),
        Writers = ["Brian K. Vaughan"],
        Artists = ["Fiona Staples"],
        Summary = "Marko and Alana arrive on Quietus.",
        Barcode = "70985301744800711",
        MetronId = 4521,
        MetronSeriesId = 1234,
        CoverUrl = "https://static.metron.cloud/media/issue/2018/11/29/saga-7.jpg",
    };

    /// <summary>Daredevil #181, a key issue, typed in by hand.</summary>
    public static ComicDetails Daredevil181 => new()
    {
        Series = "Daredevil",
        SeriesYear = 1964,
        Number = "181",
        Publisher = "Marvel",
        CoverDate = new DateOnly(1982, 4, 1),
        Writers = ["Frank Miller"],
        Artists = ["Frank Miller", "Klaus Janson"],
        IsKeyIssue = true,
        KeyNote = "Death of Elektra",
    };

    /// <summary>The first Saga trade, as Open Library has it.</summary>
    public static ComicDetails SagaVolume1 => new()
    {
        Format = ComicFormat.TradePaperback,
        Title = "Saga, Volume One",
        Series = "Saga",
        Number = "1",
        Publisher = "Image Comics",
        Writers = ["Brian K. Vaughan"],
        Isbn = "9781607066019",
        CoverUrl = "https://covers.openlibrary.org/b/id/12679639-L.jpg",
    };

    public static ComicDetails Issue(string series, string number, int? year = null) => new() { Series = series, Number = number, SeriesYear = year };

    /// <summary>A Saga issue as Metron would describe it: linked to the series, no barcode.</summary>
    public static ComicDetails SagaIssue(string number) => Saga7 with
    {
        Number = number,
        Title = null,
        Barcode = null,
        MetronId = 4514 + int.Parse(number, System.Globalization.CultureInfo.InvariantCulture),
        CoverUrl = $"https://static.metron.cloud/media/issue/saga-{number}.jpg",
    };
}
