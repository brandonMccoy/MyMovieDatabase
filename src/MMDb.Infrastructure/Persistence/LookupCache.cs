using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MMDb.Infrastructure.Persistence;

/// <summary>A saved answer from an online database, so the same question isn't asked twice.</summary>
internal sealed class LookupCacheEntry
{
    public required string Provider { get; init; }

    /// <summary>The request, without any API key.</summary>
    public required string Key { get; init; }

    public required string Json { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}

internal sealed class LookupCacheEntryConfiguration : IEntityTypeConfiguration<LookupCacheEntry>
{
    public void Configure(EntityTypeBuilder<LookupCacheEntry> entry)
    {
        entry.ToTable("LookupCache");
        entry.HasKey(e => new { e.Provider, e.Key });
    }
}

/// <summary>
/// The lookup cache table. A singleton (HTTP handlers outlive DI scopes), so each call uses its own
/// short-lived context; that also makes it safe for parallel lookups.
/// </summary>
internal sealed class LookupCache(DbContextOptions<ShelfDbContext> options)
{
    /// <returns>The saved JSON if it was fetched at or after <paramref name="notBefore"/>.</returns>
    public async Task<string?> FindAsync(string provider, string key, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        await using var db = new ShelfDbContext(options);
        var entry = await db.LookupCache.AsNoTracking().FirstOrDefaultAsync(e => e.Provider == provider && e.Key == key, cancellationToken);
        return entry is not null && entry.FetchedAt >= notBefore ? entry.Json : null;
    }

    public async Task StoreAsync(string provider, string key, string json, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        await using var db = new ShelfDbContext(options);
        var entry = await db.LookupCache.FirstOrDefaultAsync(e => e.Provider == provider && e.Key == key, cancellationToken);
        if (entry is null)
        {
            db.LookupCache.Add(new() { Provider = provider, Key = key, Json = json, FetchedAt = fetchedAt });
        }
        else
        {
            entry.Json = json;
            entry.FetchedAt = fetchedAt;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same request finished in parallel and was saved first; its answer is as good as this one.
        }
    }
}
