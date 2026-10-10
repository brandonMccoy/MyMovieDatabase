namespace MMDb.UI;

/// <summary>The system's open-a-file dialog, for video files. Provided by the PC app; screens hide file choices without it.</summary>
public interface IVideoFilePicker
{
    /// <returns>The chosen file's full path, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickVideoFileAsync();
}
