namespace MMDb.UI;

/// <summary>The system's choose-a-folder dialog. Provided by the PC app; screens hide folder choices without it.</summary>
public interface IFolderPicker
{
    /// <returns>The chosen folder's full path, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickFolderAsync();
}
