using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MMDb.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to build the model when adding migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShelfDbContext>
{
    public ShelfDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShelfDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
