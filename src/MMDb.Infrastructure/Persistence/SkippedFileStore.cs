using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Application.Digital;

namespace MMDb.Infrastructure.Persistence;

internal sealed class SkippedFile
{
    public required string Path { get; init; }

    public required DateTimeOffset SkippedAt { get; init; }
}

internal sealed class SkippedFileConfiguration : IEntityTypeConfiguration<SkippedFile>
{
    public void Configure(EntityTypeBuilder<SkippedFile> file)
    {
        file.ToTable("SkippedFiles");
        file.HasKey(f => f.Path);

        // Windows paths: "D:\Movies\Heat.mkv" and "d:\movies\heat.mkv" are one file.
        file.Property(f => f.Path).UseCollation("NOCASE");
    }
}

/// <summary>Skipped files in the database, so they are backed up with the collection. Each call uses its own context.</summary>
internal sealed class SkippedFileStore(DbContextOptions<ShelfDbContext> options, TimeProvider clock) : ISkippedFileStore
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        return await db.SkippedFiles.AsNoTracking().Select(f => f.Path).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        if (!await db.SkippedFiles.AnyAsync(f => f.Path == path, cancellationToken))
        {
            db.SkippedFiles.Add(new() { Path = path, SkippedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        await db.SkippedFiles.Where(f => f.Path == path).ExecuteDeleteAsync(cancellationToken);
    }
}
