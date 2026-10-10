namespace MMDb.UI.Scanning;

public enum ScanTarget
{
    /// <summary>The barcode on a film's case or a comic's cover (UPC, EAN, with add-on).</summary>
    Product,

    /// <summary>The pairing QR code the PC shows.</summary>
    PairingCode,
}

public enum ScannerStart
{
    Started,

    /// <summary>The user didn't allow the camera.</summary>
    NotAllowed,

    /// <summary>The device has no camera to use.</summary>
    NoCamera,
}

/// <summary>
/// The phone's camera as a barcode scanner. While it runs, the camera shows behind the page, so the scan
/// screen keeps its background see-through. Registered by the phone app only.
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>Asks for the camera if needed and starts reading codes.</summary>
    /// <param name="onCode">Called with the text of each code read, possibly many times for one barcode held in view.</param>
    Task<ScannerStart> StartAsync(ScanTarget target, Action<string> onCode);

    Task StopAsync();
}
