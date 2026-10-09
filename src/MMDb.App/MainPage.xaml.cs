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
#endif
    }
}
