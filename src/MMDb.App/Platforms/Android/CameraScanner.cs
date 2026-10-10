using BarcodeScanning;
using MMDb.UI.Scanning;

namespace MMDb.App;

/// <summary>
/// <see cref="IBarcodeScanner"/> over the phone's camera (Google ML Kit through BarcodeScanning.Native.Maui).
/// The camera view sits behind the web view on <see cref="MainPage"/>; the scan screen is see-through over it.
/// ML Kit reads a barcode but not the add-on beside it, so for products the frame it was read in is looked at
/// again by <see cref="AddOnReader"/>.
/// </summary>
internal sealed class CameraScanner : IBarcodeScanner
{
    private const BarcodeFormats ProductCodes = BarcodeFormats.Upca | BarcodeFormats.Upce | BarcodeFormats.Ean13 | BarcodeFormats.Ean8;

    /// <summary>An add-on and the gap before it reach at most this far past the barcode, as a share of its length.</summary>
    private const float AddOnReach = 0.8f;

    private CameraView? _camera;
    private Action<string>? _onCode;
    private ProductReadings? _readings;
    private bool _readAddOns = true;

    // The barcodes read in the frame that is about to be handed over.
    private IReadOnlySet<BarcodeResult>? _inFrame;
    private int[] _pixels = [];
    private byte[] _gray = [];

    public bool ReadAddOns
    {
        get => _readAddOns;
        set
        {
            _readAddOns = value;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _readings?.AddOns = value;
                _camera?.CaptureNextFrame = WantsFrames;
            });
        }
    }

    private bool WantsFrames => _readings is { AddOns: true };

    /// <summary>Called by <see cref="MainPage"/> once its camera view exists.</summary>
    public void Attach(CameraView camera)
    {
        _camera = camera;
        camera.OnDetectionFinished += Detected;
        camera.OnImageCaptured += Captured;
    }

    public Task<ScannerStart> StartAsync(ScanTarget target, Action<string> onCode) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (_camera is null)
        {
            return ScannerStart.NoCamera;
        }

        if (!await Methods.AskForRequiredPermissionAsync())
        {
            return ScannerStart.NotAllowed;
        }

        _onCode = onCode;
        _readings?.Dispose();
        _readings = target == ScanTarget.Product ? new ProductReadings(TimeProvider.System, onCode) { AddOns = _readAddOns } : null;
        _camera.BarcodeSymbologies = target == ScanTarget.PairingCode ? BarcodeFormats.QRCode : ProductCodes;
        _camera.CaptureNextFrame = WantsFrames;
        _camera.Opacity = 1;
        _camera.PauseScanning = false;
        _camera.CameraEnabled = true;
        return ScannerStart.Started;
    });

    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        _onCode = null;
        _readings?.Dispose();
        _readings = null;
        _inFrame = null;
        if (_camera is not null)
        {
            _camera.CaptureNextFrame = false;
            _camera.CameraEnabled = false;
            _camera.Opacity = 0;
        }
    });

    private void Detected(object? sender, OnDetectionFinishedEventArg e)
    {
        if (_onCode is not { } onCode)
        {
            return;
        }

        if (_readings is null)
        {
            foreach (var barcode in e.BarcodeResults)
            {
                if (!string.IsNullOrWhiteSpace(barcode.DisplayValue))
                {
                    onCode(barcode.DisplayValue);
                }
            }
        }
        else if (WantsFrames && e.BarcodeResults.Count > 0)
        {
            // The frame they were read in comes next (Captured), to look for add-ons in.
            _inFrame = e.BarcodeResults;
        }
        else
        {
            Report(e.BarcodeResults, frame: null);
        }
    }

    private void Captured(object? sender, OnImageCapturedEventArg e)
    {
        using var image = e.Image;
        var read = _inFrame;
        _inFrame = null;
        try
        {
            if (read is not null)
            {
                Report(read, image.PlatformRepresentation);
            }
        }
        finally
        {
            // The camera view hands over one frame per asking.
            _camera?.CaptureNextFrame = WantsFrames;
        }
    }

    private void Report(IReadOnlySet<BarcodeResult> barcodes, Android.Graphics.Bitmap? frame)
    {
        foreach (var barcode in barcodes)
        {
            if (_readings is not { } readings || string.IsNullOrWhiteSpace(barcode.DisplayValue))
            {
                continue;
            }

            var addOn = frame is not null && readings.MayHaveAddOn(barcode.DisplayValue) ? ReadAddOn(frame, barcode.ImageBoundingBox) : null;
            readings.Read(barcode.DisplayValue, addOn);
        }
    }

    private string? ReadAddOn(Android.Graphics.Bitmap frame, RectF box)
    {
        // Only the strip the add-on can be in is copied out of the frame: the barcode's box, stretched along
        // its longer side, which is the way it reads.
        var alongX = box.Width >= box.Height;
        var reach = (alongX ? box.Width : box.Height) * AddOnReach;
        var left = Math.Clamp((int)(box.Left - (alongX ? reach : 0)), 0, frame.Width);
        var top = Math.Clamp((int)(box.Top - (alongX ? 0 : reach)), 0, frame.Height);
        var width = Math.Clamp((int)(box.Right + (alongX ? reach : 0)), 0, frame.Width) - left;
        var height = Math.Clamp((int)(box.Bottom + (alongX ? 0 : reach)), 0, frame.Height) - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var count = width * height;
        if (_pixels.Length < count)
        {
            (_pixels, _gray) = (new int[count], new byte[count]);
        }

        frame.GetPixels(_pixels, 0, width, left, top, width, height);
        for (var i = 0; i < count; i++)
        {
            // Red stands in for brightness, as in a shop's scanner: black bars stand out on white and on the
            // reds and yellows barcodes get printed on.
            _gray[i] = (byte)(_pixels[i] >> 16);
        }

        return AddOnReader.Read(_gray.AsSpan(0, count), width, height, new((int)box.Left - left, (int)box.Top - top, (int)box.Width, (int)box.Height));
    }
}
