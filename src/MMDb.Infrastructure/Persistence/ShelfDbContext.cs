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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IndexPeople(onlyMissing: false);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IndexPeople(onlyMissing: false);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills in the searchable people column for films saved before there was one.</summary>
    internal async Task IndexPeopleAsync(CancellationToken cancellationToken)
    {
        await Movies.Where(m => EF.Property<string?>(m, PeopleText.Column) == null).LoadAsync(cancellationToken);
        IndexPeople(onlyMissing: true);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        ChangeTracker.Clear();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShelfDbContext).Assembly);

    // Keeps each film's people column in step with its cast and crew.
    private void IndexPeople(bool onlyMissing)
    {
        foreach (var entry in ChangeTracker.Entries<Movie>())
        {
            var people = entry.Property<string?>(PeopleText.Column);
            if (onlyMissing ? people.CurrentValue is null : entry.State is EntityState.Added or EntityState.Modified)
            {
                people.CurrentValue = PeopleText.Of(entry.Entity);
            }
        }
    }
}
