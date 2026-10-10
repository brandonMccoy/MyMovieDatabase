using Microsoft.EntityFrameworkCore;
using MMDb.Domain.Comics;
using MMDb.Domain.Devices;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

/// <summary>The SQLite database on the PC: the source of truth for the collection.</summary>
public sealed class ShelfDbContext(DbContextOptions<ShelfDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<Comic> Comics => Set<Comic>();

    public DbSet<ComicSeries> ComicSeries => Set<ComicSeries>();

    public DbSet<PairedDevice> PairedDevices => Set<PairedDevice>();

    internal DbSet<LookupCacheEntry> LookupCache => Set<LookupCacheEntry>();

    internal DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShelfDbContext).Assembly);
}
