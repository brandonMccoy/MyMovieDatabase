using System.Globalization;
using System.Text;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>A camera frame for the scanner tests: brightness per pixel, and where the main barcode is in it.</summary>
public sealed record TestFrame(byte[] Gray, int Width, int Height, PixelBox Box)
{
    /// <summary>The same frame with the phone turned a quarter turn clockwise, <paramref name="turns"/> times.</summary>
    public TestFrame Turned(int turns)
    {
        var frame = this;
        for (var t = 0; t < turns % 4; t++)
        {
            var (w, h) = (frame.Width, frame.Height);
            var turned = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    turned[(x * h) + (h - 1 - y)] = frame.Gray[(y * w) + x];
                }
            }

            var box = frame.Box;
            frame = new(turned, h, w, new(h - box.Top - box.Height, box.Left, box.Height, box.Width));
        }

        return frame;
    }

    public string? ReadAddOn() => AddOnReader.Read(Gray, Width, Height, Box);

    /// <summary>A recorded frame: a binary PGM under Fixtures/scan.</summary>
    public static TestFrame Recorded(string name, PixelBox box)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scan", name));
        var header = Encoding.ASCII.GetString(bytes, 0, 32).Split('\n');
        var size = header[1].Split(' ');
        var (width, height) = (int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture));
        return new(bytes[^(width * height)..], width, height, box);
    }
}

/// <summary>
/// Draws a UPC-A, with an add-on beside it if asked, the way it is printed on a cover: dark bars on a light
/// label, slightly soft and grainy as a camera sees it. The bar patterns are written out here as the standard
/// gives them, so the reader is checked against the standard and not against its own tables.
/// </summary>
public static class PrintedBarcode
{
    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] FiveDigitSets = ["GGLLL", "GLGLL", "GLLGL", "GLLLG", "LGGLL", "LLGGL", "LLLGG", "LGLGL", "LGLLG", "LLGLG"];

    /// <param name="module">The width of the narrowest bar, in pixels.</param>
    /// <param name="beside">What to draw in the add-on's place instead: bars as 1s and 0s.</param>
    public static TestFrame Draw(string upc, string? addOn = null, int module = 3, bool soft = true, string? beside = null)
    {
        var main = MainBars(upc);
        var extra = beside ?? (addOn is null ? "" : AddOn(addOn));
        const int Margin = 12, Gap = 9, BarsTall = 44, AddOnDrop = 9;
        var modulesWide = Margin + main.Length + Gap + extra.Length + Margin;
        var (width, height) = (modulesWide * module, (BarsTall + (2 * Margin)) * module);

        var gray = new byte[width * height];
        Array.Fill(gray, (byte)215);
        Bars(main, Margin, Margin);

        // An add-on's bars are shorter: its digits are printed above them.
        Bars(extra, Margin + main.Length + Gap, Margin + AddOnDrop);

        if (soft)
        {
            gray = Soften(gray, width, height);
        }

        return new(gray, width, height, new((Margin - 1) * module, Margin * module, (main.Length + 2) * module, BarsTall * module));

        void Bars(string pattern, int left, int top)
        {
            for (var m = 0; m < pattern.Length; m++)
            {
                if (pattern[m] != '1')
                {
                    continue;
                }

                for (var y = top * module; y < (Margin + BarsTall) * module; y++)
                {
                    gray.AsSpan((y * width) + ((left + m) * module), module).Fill(35);
                }
            }
        }
    }

    private static string MainBars(string upc)
    {
        var bars = new StringBuilder("101");
        for (var i = 0; i < 12; i++)
        {
            var digit = L[upc[i] - '0'];
            bars.Append(i < 6 ? digit : string.Concat(digit.Select(c => c == '1' ? '0' : '1')));
            if (i == 5)
            {
                bars.Append("01010");
            }
        }

        return bars.Append("101").ToString();
    }

    private static string AddOn(string digits)
    {
        var sets = digits.Length == 2
            ? (int.Parse(digits, CultureInfo.InvariantCulture) % 4) switch { 0 => "LL", 1 => "LG", 2 => "GL", _ => "GG" }
            : FiveDigitSets[((3 * (digits[0] + digits[2] + digits[4] - (3 * '0'))) + (9 * (digits[1] + digits[3] - (2 * '0')))) % 10];
        var bars = new StringBuilder("1011");
        for (var i = 0; i < digits.Length; i++)
        {
            bars.Append(i > 0 ? "01" : "").Append((sets[i] == 'L' ? L : G)[digits[i] - '0']);
        }

        return bars.ToString();
    }

    // A little blur and grain: no camera sees bars with clean edges.
    private static byte[] Soften(byte[] gray, int width, int height)
    {
        var soft = new byte[gray.Length];
        var grain = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (left, right) = (Math.Max(0, x - 1), Math.Min(width - 1, x + 1));
                var (up, down) = (Math.Max(0, y - 1), Math.Min(height - 1, y + 1));
                var value = ((4 * gray[(y * width) + x]) + (2 * (gray[(y * width) + left] + gray[(y * width) + right]))
                    + gray[(up * width) + x] + gray[(down * width) + x]) / 10;
                soft[(y * width) + x] = (byte)Math.Clamp(value + grain.Next(-10, 11), 0, 255);
            }
        }

        return soft;
    }
}
