namespace MMDb.Domain.Tests;

public sealed class UpcTests
{
    [Theory]
    [InlineData("786936894424")]   // UPC-A
    [InlineData("036000291452")]   // UPC-A, GS1 example
    [InlineData("4006381333931")]  // EAN-13
    [InlineData("5051892230155")]  // EAN-13
    public void Valid_barcodes_parse(string code)
    {
        Upc.TryParse(code, out var upc).ShouldBeTrue();
        upc.Value.ShouldBe(code);
    }

    [Theory]
    [InlineData("0 36000 29145 2", "036000291452")]
    [InlineData("4-006381-333931", "4006381333931")]
    [InlineData(" 786936894424 ", "786936894424")]
    public void Spaces_and_hyphens_are_ignored(string input, string expected)
    {
        Upc.Parse(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("786936894421")]   // wrong check digit
    [InlineData("4006381333932")]  // wrong check digit
    [InlineData("78693689442")]    // 11 digits
    [InlineData("78693689442400")] // 14 digits
    [InlineData("7869368944a0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Invalid_barcodes_are_rejected(string? input)
    {
        Upc.TryParse(input, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_throws_for_an_invalid_barcode()
    {
        Should.Throw<FormatException>(() => Upc.Parse("123"));
    }

    [Fact]
    public void Barcodes_with_the_same_digits_are_equal()
    {
        Upc.Parse("0 36000 29145 2").ShouldBe(Upc.Parse("036000291452"));
    }
}
