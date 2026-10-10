using System.Globalization;

namespace MMDb.UI.Components;

/// <summary>How long ago something happened, as the sidebar and Settings say it.</summary>
public static class Ago
{
    /// <returns>"just now", "5 min ago", "today at 14:03", "8 Oct at 14:03" or "8 Oct 2025".</returns>
    public static string Describe(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        var local = TimeZoneInfo.ConvertTime(at, timeZone);
        var today = TimeZoneInfo.ConvertTime(now, timeZone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today.Date)
        {
            return $"today at {time}";
        }

        return local.Year == today.Year
            ? $"{local.ToString("d MMM", CultureInfo.InvariantCulture)} at {time}"
            : local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
