namespace MMDb.Domain.Comics;

/// <summary>What kind of comic a copy is. Declared in display order.</summary>
public enum ComicFormat
{
    SingleIssue,
    TradePaperback,
    Hardcover,
    Omnibus,
}

public static class ComicFormatExtensions
{
    public static string DisplayName(this ComicFormat format) => format switch
    {
        ComicFormat.SingleIssue => "Single issue",
        ComicFormat.TradePaperback => "Trade paperback",
        ComicFormat.Hardcover => "Hardcover",
        ComicFormat.Omnibus => "Omnibus",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>Trades, hardcovers and omnibuses collect issues; they sit on their own shelf, not in a series' run.</summary>
    public static bool IsCollected(this ComicFormat format) => format != ComicFormat.SingleIssue;
}
