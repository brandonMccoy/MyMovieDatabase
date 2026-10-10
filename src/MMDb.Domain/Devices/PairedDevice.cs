namespace MMDb.Domain.Devices;

/// <summary>A phone allowed to use the collection. It proves itself with a token; the PC keeps only the token's hash.</summary>
public sealed class PairedDevice
{
    public const int MaxNameLength = 60;

    /// <summary>A phone that keeps talking isn't written to the database more often than this.</summary>
    public static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);

    // For EF Core.
    private PairedDevice()
    {
        Name = "";
        TokenHash = "";
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset PairedAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public static PairedDevice Create(string? name, string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        var trimmed = (name ?? "").Trim();
        return new PairedDevice
        {
            Id = Guid.CreateVersion7(now),
            Name = trimmed.Length == 0 ? "Phone" : trimmed[..Math.Min(trimmed.Length, MaxNameLength)],
            TokenHash = tokenHash,
            PairedAt = now,
            LastSeenAt = now,
        };
    }

    /// <returns><see langword="true"/> if this was worth writing down (see <see cref="SeenEvery"/>).</returns>
    public bool Seen(DateTimeOffset now)
    {
        if (LastSeenAt is { } last && now - last < SeenEvery)
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }
}
