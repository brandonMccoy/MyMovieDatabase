using MMDb.UI;
using Windows.Storage.Pickers;

namespace MMDb.App;

/// <summary>The Windows folder dialog, attached to Shelf's window.</summary>
internal sealed class WindowsFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");

        // An unpackaged app has to say which window owns the dialog.
        var window = (MauiWinUIWindow)Microsoft.Maui.Controls.Application.Current!.Windows[0].Handler!.PlatformView!;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    });
}
