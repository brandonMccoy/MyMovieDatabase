using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>Reading the add-on beside a comic's UPC from a camera frame, given where the UPC itself was found.</summary>
public sealed class AddOnReaderTests
{
    private const string Upc = "070989304307";

    // The corner of a 1980s DC cover photographed on the test phone: UPC 0 70989 30430 7 with the add-on 04.
    private static TestFrame Photographed => TestFrame.Recorded("comic-upc-070989304307-addon-04.pgm", new(40, 20, 396, 280));

    [Theory]
    [InlineData("00")]
    [InlineData("04")]
    [InlineData("12")]
    [InlineData("37")]
    [InlineData("58")]
    [InlineData("99")]
    public void A_two_digit_add_on_is_read(string addOn) =>
        PrintedBarcode.Draw(Upc, addOn).ReadAddOn().ShouldBe(addOn);

    [Theory]
    [InlineData("00711")]
    [InlineData("00111")]
    [InlineData("06849")]
    [InlineData("12345")]
    [InlineData("51999")]
    [InlineData("90000")]
    public void A_five_digit_add_on_is_read(string addOn) =>
        PrintedBarcode.Draw("759606200687", addOn).ReadAddOn().ShouldBe(addOn);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void It_is_read_whichever_way_up_the_phone_is_held(int quarterTurns)
    {
        PrintedBarcode.Draw(Upc, "04").Turned(quarterTurns).ReadAddOn().ShouldBe("04");
        PrintedBarcode.Draw(Upc, "00711").Turned(quarterTurns).ReadAddOn().ShouldBe("00711");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void It_is_read_near_and_far(int pixelsPerBar)
    {
        PrintedBarcode.Draw(Upc, "04", pixelsPerBar).ReadAddOn().ShouldBe("04");
        PrintedBarcode.Draw(Upc, "00711", pixelsPerBar).ReadAddOn().ShouldBe("00711");
    }

    // A 2000s DC cover: UPC 7 61941 20019 4 with the add-on 75111, printed straight onto red artwork under a glossy bag.
    private static TestFrame PhotographedOnRed => TestFrame.Recorded("comic-upc-761941200194-addon-75111.pgm", new(52, 24, 367, 216));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_add_on_on_a_photographed_cover_is_read(int quarterTurns) =>
        Photographed.Turned(quarterTurns).ReadAddOn().ShouldBe("04");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_five_digit_add_on_printed_on_coloured_artwork_is_read(int quarterTurns) =>
        PhotographedOnRed.Turned(quarterTurns).ReadAddOn().ShouldBe("75111");

    // A frame from the test phone's camera, held upright so the barcode lies on its side, as the phone's
    // barcode reader boxed it: the box takes in the near end of the add-on.
    [Fact]
    public void It_is_read_from_a_camera_frame_whose_box_runs_into_the_add_on() =>
        TestFrame.Recorded("camera-frame-upc-070989304307-addon-04.pgm", new(20, 346, 346, 433)).ReadAddOn().ShouldBe("04");

    [Theory]
    [InlineData("04", 20)]
    [InlineData("04", 60)]
    [InlineData("00711", 30)]
    [InlineData("00711", 120)]
    public void A_box_that_takes_in_some_of_the_add_on_or_all_of_it_doesnt_matter(string addOn, int extraPixels)
    {
        var frame = PrintedBarcode.Draw(Upc, addOn);

        (frame with { Box = frame.Box with { Width = frame.Box.Width + extraPixels } }).ReadAddOn().ShouldBe(addOn);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(12)]
    public void A_box_a_little_off_the_barcode_doesnt_matter(int shift)
    {
        var frame = PrintedBarcode.Draw(Upc, "04");

        (frame with { Box = frame.Box with { Left = frame.Box.Left + shift } }).ReadAddOn().ShouldBe("04");
    }

    [Fact]
    public void A_barcode_with_nothing_beside_it_has_no_add_on() =>
        PrintedBarcode.Draw(Upc).ReadAddOn().ShouldBeNull();

    [Theory]
    [InlineData("11111111111111111111")]
    [InlineData("1011000110100100011")]
    [InlineData("10110001101010011101")] // 0 and 4 printed in sets that only fit a number one more than a multiple of four
    [InlineData("1010101010101010101010101")]
    [InlineData("110011001100110011001100110011001100110011001")]
    public void Artwork_or_other_bars_beside_the_barcode_are_not_taken_for_an_add_on(string beside) =>
        PrintedBarcode.Draw(Upc, beside: beside).ReadAddOn().ShouldBeNull();

    [Fact]
    public void An_add_on_cut_off_by_the_edge_of_the_frame_is_not_guessed_at()
    {
        var whole = PrintedBarcode.Draw("759606200687", "00711");
        var width = whole.Width - (30 * 3);
        var cut = new byte[width * whole.Height];
        for (var y = 0; y < whole.Height; y++)
        {
            whole.Gray.AsSpan(y * whole.Width, width).CopyTo(cut.AsSpan(y * width));
        }

        new TestFrame(cut, width, whole.Height, whole.Box).ReadAddOn().ShouldBeNull();
    }

    [Theory]
    [InlineData(-500, 0, 300, 100)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5000, 5000, 300, 100)]
    [InlineData(10, 10, 60, 30)]
    public void A_box_that_isnt_on_a_barcode_reads_as_nothing(int left, int top, int width, int height)
    {
        var frame = PrintedBarcode.Draw(Upc, "04");

        AddOnReader.Read(frame.Gray, frame.Width, frame.Height, new(left, top, width, height)).ShouldBeNull();
    }
}
