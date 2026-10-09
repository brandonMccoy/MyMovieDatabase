using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MMDb.Infrastructure.Persistence;

/// <summary>Copies the database aside on startup and keeps the most recent copies.</summary>
public static class DatabaseBackup
{
    public const int DefaultKeep = 10;
    private const string Prefix = "shelf-";
    private const string Extension = ".db";

    /// <summary>
    /// Writes <c>shelf-yyyyMMdd-HHmmss.db</c> into <paramref name="backupDirectory"/> using SQLite's online backup
    /// (safe even with a write-ahead log), then deletes all but the newest <paramref name="keep"/> backups.
    /// Does nothing if the database does not exist yet.
    /// </summary>
    /// <returns>The new backup file, or <see langword="null"/> if there was nothing to back up.</returns>
    public static string? Run(string databasePath, string backupDirectory, TimeProvider clock, int keep = DefaultKeep)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        if (!File.Exists(databasePath))
        {
            return null;
        }

        Directory.CreateDirectory(backupDirectory);
        var stamp = clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(backupDirectory, Prefix + stamp + Extension);

        using (var source = Open(databasePath, SqliteOpenMode.ReadOnly))
        using (var destination = Open(target, SqliteOpenMode.ReadWriteCreate))
        {
            source.BackupDatabase(destination);
        }

        // The timestamp sorts chronologically, so the oldest come first by name.
        var expired = Directory.GetFiles(backupDirectory, Prefix + "*" + Extension)
            .Order(StringComparer.Ordinal)
            .SkipLast(keep);
        foreach (var old in expired)
        {
            File.Delete(old);
        }

        return target;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
