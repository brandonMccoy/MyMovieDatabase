namespace MMDb.Domain;

/// <summary>Columns shared by every collected product (movies now, comics later).</summary>
public abstract class ProductBase
{
    public Guid Id { get; protected init; }

    public DateTimeOffset CreatedAt { get; protected init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bumped on every change; used for optimistic concurrency and phone sync.</summary>
    public int Version { get; private set; }

    /// <summary>Path of the cover in the cover store, if one has been saved.</summary>
    public string? CoverImage { get; protected set; }

    /// <summary>Records which stored cover belongs to this product. Saved together with the change that brought it.</summary>
    public void UseCover(string? coverImage) => CoverImage = coverImage;

    protected void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
