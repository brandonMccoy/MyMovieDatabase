namespace MMDb.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if WINDOWS
        blazorWebView.BlazorWebViewInitialized += (_, e) =>
        {
            if (CoverFolder.Path is { } covers)
            {
                Directory.CreateDirectory(covers);
                e.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    CoverFolder.HostName, covers, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }
        };
#elif ANDROID
        // The camera sits behind the web view; the scan screen leaves its background see-through. Off, it is
        // see-through rather than hidden: a view that starts hidden is never given a size when it is shown.
        var camera = new BarcodeScanning.CameraView
        {
            Opacity = 0,
            CameraEnabled = false,
            VibrationOnDetected = false,
            CaptureQuality = BarcodeScanning.CaptureQuality.High,
        };
        root.Children.Insert(0, camera);
        IPlatformApplication.Current!.Services.GetRequiredService<CameraScanner>().Attach(camera);

        blazorWebView.BackgroundColor = Colors.Transparent;
        blazorWebView.BlazorWebViewInitialized += (_, e) => e.WebView.SetBackgroundColor(Android.Graphics.Color.Transparent);

        // The pages ask for stored covers under the same host name as on the PC; the phone answers from its own
        // cover folder, fetching from the PC what isn't there yet. A page showing one cover large asks with ?full=1.
        var covers = IPlatformApplication.Current.Services.GetRequiredService<MMDb.Client.CoverShelf>();
        blazorWebView.WebResourceRequested += (_, e) =>
        {
            if (e.Uri.Host != CoverFolder.HostName)
            {
                return;
            }

            var name = Uri.UnescapeDataString(e.Uri.AbsolutePath.TrimStart('/'));
            e.Handled = true;
            e.SetResponse(200, "OK", ContentTypeOf(name), LoadAsync(covers, name, full: e.Uri.Query == "?full=1"));
        };
#endif
    }

#if ANDROID
    // A cover that can't be had comes back empty; the page then shows its placeholder.
    private static async Task<Stream?> LoadAsync(MMDb.Client.CoverShelf covers, string name, bool full) =>
        await covers.GetAsync(name, full) is { } cover ? new MemoryStream(cover.Bytes) : new MemoryStream();

    private static string ContentTypeOf(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".PNG" => "image/png",
        ".WEBP" => "image/webp",
        ".GIF" => "image/gif",
        _ => "image/jpeg",
    };
#endif
}
