using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Application.Vault;

namespace MMDb.Infrastructure.Persistence;

internal sealed class Setting
{
    public required string Key { get; init; }

    public required string Value { get; set; }
}

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> setting)
    {
        setting.ToTable("Settings");
        setting.HasKey(s => s.Key);
        setting.Property(s => s.Key).HasMaxLength(64);
    }
}

/// <summary>Settings in the database, so they are backed up with the collection. Each call uses its own context.</summary>
internal sealed class SettingStore(DbContextOptions<ShelfDbContext> options) : ISettingStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        return (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken))?.Value;
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (value is null)
        {
            if (setting is not null)
            {
                db.Settings.Remove(setting);
            }
        }
        else if (setting is null)
        {
            db.Settings.Add(new() { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
