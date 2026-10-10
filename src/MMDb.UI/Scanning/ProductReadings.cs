namespace MMDb.UI.Scanning;

/// <summary>
/// Turns what the camera reads frame by frame into the codes to report. A comic's add-on isn't read in every
/// frame (out of view, out of focus), so a UPC read without one waits a moment for a frame that has it; and
/// once it has been read with its add-on, frames that miss the add-on aren't taken for another product.
/// </summary>
public sealed class ProductReadings(TimeProvider clock, Action<string> report) : IDisposable
{
    /// <summary>How long a UPC read without an add-on waits for a frame that shows one.</summary>
    public static readonly TimeSpan WaitForAddOn = TimeSpan.FromMilliseconds(600);

    /// <summary>How long after a UPC was read with its add-on the UPC alone is still taken for the same barcode.</summary>
    public static readonly TimeSpan RememberedFor = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private string? _waiting;
    private ITimer? _timer;
    private string? _complete;
    private DateTimeOffset _completeSeenAt;

    // Two digits are only loosely checked by how they are printed, so they count once two frames agree on them.
    private (string Code, string AddOn)? _seenOnce;

    /// <summary>Off while only films are scanned: a film's case has no add-on, so nothing waits for one.</summary>
    public bool AddOns { get; set; } = true;

    /// <summary>
    /// Whether an add-on may be printed beside this code: a UPC or EAN, but not a book's ISBN, whose add-on is
    /// the price.
    /// </summary>
    public bool MayHaveAddOn(string code) =>
        AddOns && code is { Length: 12 or 13 } && code.All(char.IsAsciiDigit)
        && !(code.Length == 13 && (code.StartsWith("978", StringComparison.Ordinal) || code.StartsWith("979", StringComparison.Ordinal)));

    /// <param name="code">The barcode read in a frame.</param>
    /// <param name="addOn">The add-on read beside it in the same frame, if any.</param>
    public void Read(string code, string? addOn)
    {
        string? first = null, second;
        lock (_gate)
        {
            if (_waiting is not null && _waiting != code)
            {
                // The camera has moved on to another barcode: the one waiting has had its chance.
                first = StopWaiting();
            }

            if (!MayHaveAddOn(code))
            {
                second = code;
            }
            else if (addOn is not null && (addOn.Length != 2 || _seenOnce == (code, addOn)))
            {
                StopWaiting();
                _seenOnce = null;
                (_complete, _completeSeenAt) = (code, clock.GetUtcNow());
                second = code + addOn;
            }
            else if (_complete == code && clock.GetUtcNow() - _completeSeenAt < RememberedFor)
            {
                _completeSeenAt = clock.GetUtcNow();
                second = null;
            }
            else
            {
                _seenOnce = addOn is null ? _seenOnce : (code, addOn);
                if (_waiting is null)
                {
                    _waiting = code;
                    _timer = clock.CreateTimer(_ => GiveUpOn(code), null, WaitForAddOn, Timeout.InfiniteTimeSpan);
                }

                second = null;
            }
        }

        Report(first);
        Report(second);
    }

    private void GiveUpOn(string code)
    {
        string? plain;
        lock (_gate)
        {
            plain = _waiting == code ? StopWaiting() : null;
        }

        Report(plain);
    }

    private string? StopWaiting()
    {
        var waiting = _waiting;
        _waiting = null;
        _timer?.Dispose();
        _timer = null;
        return waiting;
    }

    private void Report(string? code)
    {
        if (code is not null)
        {
            report(code);
        }
    }

    /// <summary>Stops without reporting what was still waiting.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            StopWaiting();
        }
    }
}
