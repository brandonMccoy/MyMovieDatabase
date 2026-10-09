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
        return new Window(new MainPage()) { Title = "Shelf" };
    }
}
