using System.Globalization;

namespace MMDb.UI.Phone;

/// <summary>How the phone says when its copy of the collection was last brought up to date.</summary>
public static class CopyAge
{
    /// <summary>"today 8:12 AM", "yesterday 6:40 PM", "3 Oct", "28 Dec 2025".</summary>
    public static string When(DateTimeOffset at, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var then = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).Date;
        var time = then.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return (today - then.Date).Days switch
        {
            <= 0 => $"today {time}",
            1 => $"yesterday {time}",
            _ => then.ToString(then.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>"1 change", "3 changes".</summary>
    public static string Changes(int count) => count == 1 ? "1 change" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} changes";
}
