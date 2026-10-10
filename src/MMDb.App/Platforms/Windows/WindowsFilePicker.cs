using MMDb.UI;
using Windows.Storage.Pickers;

namespace MMDb.App;

/// <summary>The Windows open-a-file dialog, showing video files and attached to Shelf's window.</summary>
internal sealed class WindowsFilePicker : IVideoFilePicker
{
    // The files a movie folder is searched for (docs/ARCHITECTURE.md, Digital movies).
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv"];

    public Task<string?> PickVideoFileAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        foreach (var extension in VideoExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        // An unpackaged app has to say which window owns the dialog.
        var window = (MauiWinUIWindow)Microsoft.Maui.Controls.Application.Current!.Windows[0].Handler!.PlatformView!;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);
        return (await picker.PickSingleFileAsync())?.Path;
    });
}
