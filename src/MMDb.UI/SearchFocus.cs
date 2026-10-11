using Microsoft.AspNetCore.Components;

namespace MMDb.UI;

internal static class SearchFocus
{
    /// <summary>
    /// Puts the cursor in a search box as its screen opens, ready to type. On a phone the cursor brings the
    /// keyboard up, so there only an empty box gets it: over a search already made, the keyboard would cover
    /// the matches.
    /// </summary>
    public static async Task FocusOnOpenAsync(this ElementReference box, string? text, ShelfDevice device)
    {
        if (!device.IsPhone || string.IsNullOrWhiteSpace(text))
        {
            await box.FocusAsync();
        }
    }
}
