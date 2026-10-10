using BarcodeScanning;
using MMDb.UI.Scanning;

namespace MMDb.App;

/// <summary>
/// <see cref="IBarcodeScanner"/> over the phone's camera (Google ML Kit through BarcodeScanning.Native.Maui).
/// The camera view sits behind the web view on <see cref="MainPage"/>; the scan screen is see-through over it.
/// </summary>
internal sealed class CameraScanner : IBarcodeScanner
{
    private const BarcodeFormats ProductCodes = BarcodeFormats.Upca | BarcodeFormats.Upce | BarcodeFormats.Ean13 | BarcodeFormats.Ean8;

    private CameraView? _camera;
    private Action<string>? _onCode;

    /// <summary>Called by <see cref="MainPage"/> once its camera view exists.</summary>
    public void Attach(CameraView camera)
    {
        _camera = camera;
        camera.OnDetectionFinished += Detected;
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
        _camera.BarcodeSymbologies = target == ScanTarget.PairingCode ? BarcodeFormats.QRCode : ProductCodes;
        _camera.Opacity = 1;
        _camera.PauseScanning = false;
        _camera.CameraEnabled = true;
        return ScannerStart.Started;
    });

    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        _onCode = null;
        if (_camera is not null)
        {
            _camera.CameraEnabled = false;
            _camera.Opacity = 0;
        }
    });

    private void Detected(object? sender, OnDetectionFinishedEventArg e)
    {
        var onCode = _onCode;
        if (onCode is null)
        {
            return;
        }

        foreach (var barcode in e.BarcodeResults)
        {
            if (!string.IsNullOrWhiteSpace(barcode.DisplayValue))
            {
                onCode(barcode.DisplayValue);
            }
        }
    }
}
