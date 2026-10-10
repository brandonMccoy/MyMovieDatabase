using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Application.Devices;
using MMDb.Domain.Devices;

namespace MMDb.Infrastructure.Persistence;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<PairedDevice>
{
    public void Configure(EntityTypeBuilder<PairedDevice> device)
    {
        device.ToTable("PairedDevices");
        device.HasKey(d => d.Id);
        device.Property(d => d.Name).HasMaxLength(PairedDevice.MaxNameLength);
        device.Property(d => d.TokenHash).HasMaxLength(64);
        device.HasIndex(d => d.TokenHash).IsUnique();
    }
}

internal sealed class EfDeviceRepository(ShelfDbContext db) : IDeviceRepository
{
    public Task<PairedDevice?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PairedDevices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<PairedDevice?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.PairedDevices.FirstOrDefaultAsync(d => d.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.PairedDevices.AsNoTracking().ToListAsync(cancellationToken);

    public void Add(PairedDevice device) => db.PairedDevices.Add(device);

    public void Remove(PairedDevice device) => db.PairedDevices.Remove(device);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
