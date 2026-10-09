using Microsoft.EntityFrameworkCore;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

/// <summary>The SQLite database on the PC: the source of truth for the collection.</summary>
public sealed class ShelfDbContext(DbContextOptions<ShelfDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShelfDbContext).Assembly);
}
