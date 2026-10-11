using MMDb.Server.Http;
using SkiaSharp;

namespace MMDb.Server.Tests;

/// <summary>The smaller copies of covers a phone keeps.</summary>
public sealed class CoverSizesTests
{
    private static byte[] Picture(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0xE8, 0xB0, 0x4B));
        }

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    [Theory]
    [InlineData("heat.jpg", SKEncodedImageFormat.Jpeg)]
    [InlineData("HEAT.JPEG", SKEncodedImageFormat.Jpeg)]
    [InlineData("saga 7.png", SKEncodedImageFormat.Png)]
    [InlineData("saga 7.webp", SKEncodedImageFormat.Webp)]
    public void A_stored_cover_is_scaled_down_to_medium_width_in_its_own_format(string name, SKEncodedImageFormat format)
    {
        var full = Picture(500, 750, format);

        var medium = CoverSizes.Medium(full, name);

        using var codec = SKCodec.Create(new MemoryStream(medium));
        codec.EncodedFormat.ShouldBe(format);
        codec.Info.Width.ShouldBe(342);
        codec.Info.Height.ShouldBe(513);
        medium.Length.ShouldBeLessThan(full.Length);
    }

    [Fact]
    public void A_cover_that_is_small_already_is_left_as_it_is()
    {
        var small = Picture(300, 450, SKEncodedImageFormat.Jpeg);

        CoverSizes.Medium(small, "small.jpg").ShouldBeSameAs(small);
    }

    [Theory]
    [InlineData("cover.gif")]
    [InlineData("cover.jpg")]
    public void Something_that_isnt_a_picture_it_can_scale_is_left_as_it_is(string name)
    {
        byte[] notAPicture = [1, 2, 3];

        CoverSizes.Medium(notAPicture, name).ShouldBeSameAs(notAPicture);
    }
}
