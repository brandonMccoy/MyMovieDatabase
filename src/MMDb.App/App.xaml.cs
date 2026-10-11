namespace MMDb.App;

// Qualified: MMDb.App also sees the MMDb.Application namespace.
public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Shelf" };
#if ANDROID
        // Back in front (perhaps back home): send what was saved meanwhile and bring the copy up to date.
        window.Resumed += (_, _) => _ = IPlatformApplication.Current?.Services.GetService<MMDb.Contracts.IPhoneSync>()?.SyncAsync();
#endif
        return window;
    }
}
