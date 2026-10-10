namespace MMDb.UI;

/// <summary>
/// Which of the two apps the screens run in. The PC owns the collection and can edit everything; the phone
/// browses, scans and adds, over the home network.
/// </summary>
public sealed record ShelfDevice(bool IsPhone)
{
    public static ShelfDevice Pc { get; } = new(false);

    public static ShelfDevice Phone { get; } = new(true);
}
