using SkiaSharp;

namespace MMDb.Server.Http;

/// <summary>Smaller copies of stored covers, for a phone that keeps every one of them.</summary>
public static class CoverSizes
{
    /// <summary>
    /// Sharp three across on a phone's screen, at about a third of the stored cover's size (TMDB's own
    /// in-between size is 342 as well).
    /// </summary>
    public const int MediumWidth = 342;

    /// <summary>The cover at <see cref="MediumWidth"/>, in the format its name says.</summary>
    /// <returns>The cover as it is if it is no wider than that already, or isn't a picture that can be read.</returns>
    public static byte[] Medium(byte[] image, string name)
    {
        ArgumentNullException.ThrowIfNull(image);
        var format = Path.GetExtension(name).ToUpperInvariant() switch
        {
            ".PNG" => SKEncodedImageFormat.Png,
            ".WEBP" => SKEncodedImageFormat.Webp,
            ".JPG" or ".JPEG" => SKEncodedImageFormat.Jpeg,
            _ => (SKEncodedImageFormat?)null,
        };
        if (format is null)
        {
            return image;
        }

        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= MediumWidth)
        {
            return image;
        }

        using var full = SKBitmap.Decode(codec);
        if (full is null)
        {
            return image;
        }

        var height = (int)Math.Round(full.Height * (double)MediumWidth / full.Width);
        using var small = full.Resize(new SKImageInfo(MediumWidth, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var encoded = small?.Encode(format.Value, 82);
        return encoded?.ToArray() ?? image;
    }
}
