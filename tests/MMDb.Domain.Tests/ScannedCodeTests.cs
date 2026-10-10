using MMDb.Domain.Devices;

namespace MMDb.Domain.Tests;

public sealed class ScannedCodeTests
{
    [Theory]
    [InlineData("9781607066019", ProductKind.Comic)]      // a trade's ISBN
    [InlineData("978-1-60706-601-9", ProductKind.Comic)]
    [InlineData("70985301744800711", ProductKind.Comic)]  // UPC + 5-digit add-on
    [InlineData("70985301744800", ProductKind.Comic)]     // UPC + 2-digit add-on
    [InlineData("786936894424", ProductKind.Movie)]       // UPC alone
    [InlineData("5051892230155", ProductKind.Movie)]      // EAN-13 that isn't a book
    [InlineData("709853017448", ProductKind.Movie)]       // a comic whose add-on wasn't read can't be told apart
    [InlineData("not a barcode", ProductKind.Movie)]
    [InlineData("", ProductKind.Movie)]
    [InlineData(null, ProductKind.Movie)]
    public void The_code_says_what_kind_of_product_it_is_on(string? code, ProductKind expected)
    {
        ScannedCode.KindOf(code).ShouldBe(expected);
    }
}

public sealed class PairedDeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Pixel 8", "Pixel 8")]
    [InlineData("  Pixel 8  ", "Pixel 8")]
    [InlineData("", "Phone")]
    [InlineData(null, "Phone")]
    public void A_phone_without_a_name_is_called_phone(string? given, string expected)
    {
        PairedDevice.Create(given, "HASH", Now).Name.ShouldBe(expected);
    }

    [Fact]
    public void A_very_long_name_is_cut()
    {
        PairedDevice.Create(new string('x', 200), "HASH", Now).Name.Length.ShouldBe(PairedDevice.MaxNameLength);
    }

    [Fact]
    public void A_phone_counts_as_seen_when_it_pairs_and_then_at_most_once_a_minute()
    {
        var device = PairedDevice.Create("Pixel 8", "HASH", Now);

        device.Seen(Now.AddSeconds(59)).ShouldBeFalse();
        device.LastSeenAt.ShouldBe(Now);

        device.Seen(Now.AddSeconds(60)).ShouldBeTrue();
        device.LastSeenAt.ShouldBe(Now.AddSeconds(60));
    }
}
