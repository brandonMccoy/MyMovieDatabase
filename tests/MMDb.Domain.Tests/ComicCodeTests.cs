using MMDb.Domain.Comics;

namespace MMDb.Domain.Tests;

/// <summary>Barcodes on comic covers (UPC + add-on) and ISBNs on collected editions.</summary>
public sealed class ComicCodeTests
{
    [Theory]
    [InlineData("75960620068900711", "759606200689", "00711")]
    [InlineData("759606200689 00711", "759606200689", "00711")]
    [InlineData("7 59606 20068 9 00711", "759606200689", "00711")]
    [InlineData("759606200689", "759606200689", null)]
    [InlineData("75960620068901", "759606200689", "01")]
    [InlineData("978160706601900111", "9781607066019", "00111")]
    public void A_comic_barcode_is_the_upc_then_its_add_on(string input, string upc, string? supplement)
    {
        var barcode = ComicBarcode.Parse(input);

        barcode.Upc.Value.ShouldBe(upc);
        barcode.Supplement.ShouldBe(supplement);
        barcode.Value.ShouldBe(upc + supplement);
    }

    [Theory]
    [InlineData("00711", 7, 1, 1)]
    [InlineData("18132", 181, 3, 2)]
    [InlineData("00011", 0, 1, 1)]
    public void The_add_on_gives_issue_cover_and_printing(string supplement, int issue, int cover, int printing)
    {
        ComicBarcode.Parse("759606200689" + supplement).AddOn.ShouldBe(new ComicAddOn(issue, cover, printing));
    }

    [Theory]
    [InlineData("759606200689")]
    [InlineData("75960620068901")]
    public void Only_a_five_digit_add_on_can_be_read(string input)
    {
        ComicBarcode.Parse(input).AddOn.ShouldBeNull();
    }

    [Theory]
    [InlineData("75960620068100711")] // wrong check digit in the UPC
    [InlineData("7596062006890071")]  // 16 digits
    [InlineData("759606200689007111")] // 12 + 6
    [InlineData("75960620068900a11")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_codes_are_not_comic_barcodes(string? input)
    {
        ComicBarcode.TryParse(input, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_barcode_reads_like_the_print_under_the_bars()
    {
        ComicBarcode.Parse("75960620068900711").ToString().ShouldBe("759606200689 00711");
    }

    [Theory]
    [InlineData("9781607066019", "9781607066019")]
    [InlineData("978-1-60706-601-9", "9781607066019")]
    [InlineData("1607066017", "9781607066019")]
    [InlineData("1-60706-601-7", "9781607066019")]
    [InlineData("080442957X", "9780804429573")]
    [InlineData("080442957x", "9780804429573")]
    public void An_isbn_is_kept_as_isbn_13(string input, string expected)
    {
        Isbn.Parse(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("9781607066018")] // wrong check digit
    [InlineData("1607066018")]    // wrong check digit
    [InlineData("759606200689")]  // a UPC
    [InlineData("4006381333931")] // an EAN-13 that isn't a book
    [InlineData("16070660X7")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_codes_are_not_isbns(string? input)
    {
        Isbn.TryParse(input, out _).ShouldBeFalse();
    }
}

public sealed class IssueNumberTests
{
    [Theory]
    [InlineData("7", "7")]
    [InlineData("#7", "7")]
    [InlineData(" # 007 ", "7")]
    [InlineData("0", "0")]
    [InlineData("0.5", "0.5")]
    [InlineData("1.MU", "1.MU")]
    [InlineData("", null)]
    [InlineData("#", null)]
    [InlineData(null, null)]
    public void Issue_numbers_are_tidied(string? input, string? expected)
    {
        IssueNumber.Normalize(input).ShouldBe(expected);
    }

    [Fact]
    public void Issue_numbers_sort_the_way_a_run_reads()
    {
        string[] numbers = ["10", "Annual 1", "2", "1.MU", "½", "1", "0", "25AU", "9"];

        numbers.Order(IssueNumber.Comparer).ShouldBe(["0", "½", "1", "1.MU", "2", "9", "10", "25AU", "Annual 1"]);
    }
}

public sealed class ComicSearchTextTests
{
    [Theory]
    [InlineData("saga 7", "saga", null, "7")]
    [InlineData("Saga #7", "Saga", null, "7")]
    [InlineData("Daredevil 1964 181", "Daredevil", 1964, "181")]
    [InlineData("Daredevil (1964) #181", "Daredevil", 1964, "181")]
    [InlineData("Daredevil 1964", "Daredevil", 1964, null)]
    [InlineData("Batman", "Batman", null, null)]
    [InlineData("Absolute Batman 1", "Absolute Batman", null, "1")]
    [InlineData("X-Men 1.MU", "X-Men", null, "1.MU")]
    [InlineData("Saga #2012", "Saga", null, "2012")]
    [InlineData("1602", "1602", null, null)]
    [InlineData("1602 3", "1602", null, "3")]
    [InlineData("  saga   7 ", "saga", null, "7")]
    [InlineData("", "", null, null)]
    public void Typed_text_splits_into_series_year_and_issue(string input, string series, int? year, string? number)
    {
        ComicSearchText.Parse(input).ShouldBe(new ComicSearchText(series, year, number));
    }
}
