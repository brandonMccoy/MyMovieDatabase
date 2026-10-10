namespace MMDb.UI.Scanning;

/// <summary>Where a barcode was found in a camera frame, in pixels.</summary>
public readonly record struct PixelBox(int Left, int Top, int Width, int Height);

/// <summary>
/// Reads the 2- or 5-digit add-on printed beside a UPC or EAN barcode (on a comic: the issue, cover and
/// printing) from a camera frame. The phone's barcode reader finds and reads the main barcode but takes no
/// notice of add-ons, so this looks just past either end of the barcode it found: the add-on is to the right
/// of the barcode as printed, which can be any side of it in the frame.
/// </summary>
public static class AddOnReader
{
    /// <summary>A UPC-A or EAN-13 is this many bar widths ("modules") from its first bar to its last.</summary>
    private const int MainModules = 95;

    /// <summary>The bars and the spaces between them in a UPC-A or EAN-13.</summary>
    private const int MainRuns = 59;

    /// <summary>Lines read across the bars, spread over the barcode's height: an add-on is shorter than the barcode.</summary>
    private const int Lines = 15;

    /// <summary>The most changes of colour followed along one line.</summary>
    private const int MaxEdges = 512;

    // The widths of space, bar, space, bar for each digit in the "L" set; the "G" set is the same backwards.
    private static readonly int[][] Digits =
    [
        [3, 2, 1, 1], [2, 2, 2, 1], [2, 1, 2, 2], [1, 4, 1, 1], [1, 1, 3, 2],
        [1, 2, 3, 1], [1, 1, 1, 4], [1, 3, 1, 2], [1, 2, 1, 3], [3, 1, 1, 2],
    ];

    private static readonly int[] StartGuard = [1, 1, 2];

    // Which of a 5-digit add-on's digits are in the G set (a set bit, first digit highest), by its check digit.
    private static readonly int[] FiveDigitParity = [0x18, 0x14, 0x12, 0x11, 0x0C, 0x06, 0x03, 0x0A, 0x09, 0x05];

    /// <param name="gray">The frame, one byte of brightness per pixel, row after row.</param>
    /// <param name="barcode">The main barcode's bounding box in the frame.</param>
    /// <returns>The add-on's digits, or <see langword="null"/> if none could be read with confidence.</returns>
    public static string? Read(ReadOnlySpan<byte> gray, int width, int height, PixelBox barcode)
    {
        if (width <= 0 || height <= 0 || gray.Length < width * height || barcode.Width < 2 || barcode.Height < 2)
        {
            return null;
        }

        // Bars run across the barcode's longer side.
        var frame = new Frame(gray, width, height, alongX: barcode.Width >= barcode.Height);
        var (from, span) = frame.AlongX ? (barcode.Left, barcode.Width) : (barcode.Top, barcode.Height);
        var (acrossFrom, acrossSpan) = frame.AlongX ? (barcode.Top, barcode.Height) : (barcode.Left, barcode.Width);

        // The box is only roughly where the barcode is, and may take in some of the add-on: each line finds
        // the barcode's own first and last bar for itself, looking a little past the box either way.
        var start = Math.Max(0, from - (span / 3));
        var end = Math.Min(frame.Along - 1, from + span + (span / 3));

        Span<int> edges = stackalloc int[MaxEdges];
        Span<int> runs = stackalloc int[MaxRuns];
        var votes = new Dictionary<string, int>();
        for (var line = 0; line < Lines; line++)
        {
            var across = acrossFrom + (int)(acrossSpan * (line + 1) / (Lines + 1.0));
            if (across < 0 || across >= frame.Across || !TryFindMain(frame, across, start, end, edges, out var main))
            {
                continue;
            }

            foreach (var direction in (ReadOnlySpan<int>)[1, -1])
            {
                var origin = direction > 0 ? main.Last + 1 : main.First - 1;
                if (ReadLine(frame, across, origin, direction, main, runs) is { } digits)
                {
                    votes[digits] = votes.GetValueOrDefault(digits) + 1;
                }
            }
        }

        // Two digits are only loosely checked by how they are printed, so more lines have to agree on them.
        var best = votes.OrderByDescending(v => v.Value).FirstOrDefault();
        var needed = best.Key?.Length == 2 ? 4 : 2;
        return best.Key is not null && best.Value >= needed && votes.Count(v => v.Value == best.Value) == 1 ? best.Key : null;
    }

    /// <summary>The most bars and spaces an add-on has: a start guard, five digits and the marks between them.</summary>
    private const int MaxRuns = 3 + (5 * 4) + (4 * 2);

    private readonly ref struct Frame(ReadOnlySpan<byte> gray, int width, int height, bool alongX)
    {
        private readonly ReadOnlySpan<byte> _gray = gray;

        public bool AlongX { get; } = alongX;

        /// <summary>The frame's size along the direction the barcode is read in, and across it.</summary>
        public int Along { get; } = alongX ? width : height;

        public int Across { get; } = alongX ? height : width;

        public byte this[int along, int across] => AlongX ? _gray[(across * width) + along] : _gray[(along * width) + across];
    }

    /// <param name="First">The first dark pixel of the main barcode along the line.</param>
    /// <param name="Module">The width of its narrowest bar, in pixels.</param>
    /// <param name="Threshold">Darker than this is a bar.</param>
    private readonly record struct MainCode(int First, int Last, double Module, int Threshold);

    /// <summary>
    /// Finds the main barcode along one line by its build: 30 bars and the 29 spaces between them, 95 bar
    /// widths in all, with guard bars at both ends and in the middle, twelve digits of seven bar widths each,
    /// and blank margins before and after.
    /// </summary>
    private static bool TryFindMain(Frame frame, int across, int start, int end, Span<int> edges, out MainCode main)
    {
        main = default;
        int min = 255, max = 0;
        for (var i = start; i <= end; i++)
        {
            min = Math.Min(min, frame[i, across]);
            max = Math.Max(max, frame[i, across]);
        }

        if (max - min < 40)
        {
            return false;
        }

        // Where the line changes colour. Run r goes from edges[r] to edges[r + 1].
        var threshold = (min + max) / 2;
        var firstIsDark = frame[start, across] < threshold;
        var dark = firstIsDark;
        var count = 0;
        edges[count++] = start;
        for (var i = start + 1; i <= end && count < edges.Length - 1; i++)
        {
            if (frame[i, across] < threshold != dark)
            {
                edges[count++] = i;
                dark = !dark;
            }
        }

        edges[count] = end + 1;
        var best = double.MaxValue;
        for (var first = firstIsDark ? 2 : 1; first + MainRuns < count; first += 2)
        {
            var module = (edges[first + MainRuns] - edges[first]) / (double)MainModules;
            if (module < 1.2
                || edges[first] - edges[first - 1] < module * 2.5
                || edges[first + MainRuns + 1] - edges[first + MainRuns] < module * 2.5
                || !IsGuard(edges, first, 3, module) || !IsGuard(edges, first + 27, 5, module) || !IsGuard(edges, first + 56, 3, module))
            {
                continue;
            }

            // How far the twelve digits are from seven bar widths each.
            var off = 0.0;
            for (var digit = 0; digit < 12 && off < best; digit++)
            {
                var at = first + (digit < 6 ? 3 : 8) + (digit * 4);
                var difference = Math.Abs(((edges[at + 4] - edges[at]) / module) - 7);
                off = difference > 1.3 ? double.MaxValue : off + difference;
            }

            if (off < best)
            {
                best = off;
                main = new(edges[first], edges[first + MainRuns] - 1, module, threshold);
            }
        }

        return best < double.MaxValue;
    }

    /// <summary>Whether the runs from <paramref name="first"/> are each about one bar width wide.</summary>
    private static bool IsGuard(ReadOnlySpan<int> edges, int first, int runs, double module)
    {
        for (var r = first; r < first + runs; r++)
        {
            var width = edges[r + 1] - edges[r];
            if (width < module * 0.3 || width > module * 1.9)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads one line outward from the end of the main barcode.</summary>
    private static string? ReadLine(Frame frame, int across, int origin, int direction, MainCode main, Span<int> runs)
    {
        var module = main.Module;
        var limit = (int)(module * 75);
        var extent = frame.Along;
        var position = origin;
        var walked = 0;

        // The last of the main barcode's own bars, if this line still crosses it.
        for (; InFrame() && walked < module * 3 && frame[position, across] < main.Threshold; Step())
        {
        }

        // The blank gap before the add-on.
        var gap = 0;
        for (; InFrame() && walked < limit && frame[position, across] >= main.Threshold; Step())
        {
            gap++;
        }

        if (gap < module * 3 || gap > module * 20 || !InFrame())
        {
            return null;
        }

        // Its bars and spaces, up to the blank margin after it.
        var count = 0;
        var dark = true;
        var run = 0;
        var closed = false;
        for (; InFrame() && walked < limit; Step())
        {
            if (frame[position, across] < main.Threshold == dark)
            {
                run++;
                if (run > module * 5.5)
                {
                    // A bar is never this wide; a space this wide is the margin after the add-on.
                    closed = !dark;
                    break;
                }

                continue;
            }

            if (count == runs.Length)
            {
                return null;
            }

            runs[count++] = run;
            dark = !dark;
            run = 1;
        }

        if (!closed || count is not (13 or 31))
        {
            return null;
        }

        return Decode(runs[..count], module);

        bool InFrame() => position >= 0 && position < extent;

        void Step()
        {
            position += direction;
            walked++;
        }
    }

    private static string? Decode(ReadOnlySpan<int> runs, double mainModule)
    {
        var length = runs.Length == 13 ? 2 : 5;
        var total = 0;
        foreach (var run in runs)
        {
            total += run;
        }

        // Printed at the same scale as the barcode beside it.
        var module = total / (double)(length == 2 ? 20 : 47);
        if (module < mainModule * 0.7 || module > mainModule * 1.4 || !Fits(runs[..3], StartGuard, out _))
        {
            return null;
        }

        Span<char> digits = stackalloc char[length];
        var parity = 0;
        for (var i = 0; i < length; i++)
        {
            var at = 3 + (i * 6);
            if (i > 0 && (TooFar(runs[at - 2], module) || TooFar(runs[at - 1], module)))
            {
                return null;
            }

            var (digit, inG) = BestDigit(runs.Slice(at, 4));
            if (digit < 0)
            {
                return null;
            }

            digits[i] = (char)('0' + digit);
            parity = (parity << 1) | (inG ? 1 : 0);
        }

        // Which set each digit is printed in says what the digits must add up to.
        var valid = length == 2
            ? (((digits[0] - '0') * 10) + (digits[1] - '0')) % 4 == parity
            : FiveDigitParity[((3 * (digits[0] + digits[2] + digits[4] - (3 * '0'))) + (9 * (digits[1] + digits[3] - (2 * '0')))) % 10] == parity;
        return valid ? new string(digits) : null;

        static bool TooFar(int run, double module) => run < module * 0.3 || run > module * 1.9;
    }

    private static (int Digit, bool InG) BestDigit(ReadOnlySpan<int> runs)
    {
        Span<int> backwards = [runs[3], runs[2], runs[1], runs[0]];
        var (digit, inG, best) = (-1, false, double.MaxValue);
        for (var d = 0; d < Digits.Length; d++)
        {
            if (Fits(runs, Digits[d], out var off) && off < best)
            {
                (digit, inG, best) = (d, false, off);
            }

            if (Fits(backwards, Digits[d], out off) && off < best)
            {
                (digit, inG, best) = (d, true, off);
            }
        }

        return (digit, inG);
    }

    /// <summary>Whether the runs have the pattern's proportions; <paramref name="off"/> is how far off, in modules per module.</summary>
    private static bool Fits(ReadOnlySpan<int> runs, ReadOnlySpan<int> pattern, out double off)
    {
        int total = 0, modules = 0;
        for (var i = 0; i < runs.Length; i++)
        {
            total += runs[i];
            modules += pattern[i];
        }

        var unit = total / (double)modules;
        off = 0;
        for (var i = 0; i < runs.Length; i++)
        {
            var difference = Math.Abs(runs[i] - (pattern[i] * unit));
            if (difference > unit * 0.7)
            {
                return false;
            }

            off += difference;
        }

        off /= total;
        return off < 0.48;
    }
}
