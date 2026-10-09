using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("shelf-backup-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero));

    private string DatabasePath => Path.Combine(_root, "shelf.db");

    private string BackupDirectory => Path.Combine(_root, "backups");

    [Fact]
    public void Nothing_is_backed_up_before_the_database_exists()
    {
        DatabaseBackup.Run(DatabasePath, BackupDirectory, _clock).ShouldBeNull();

        Directory.Exists(BackupDirectory).ShouldBeFalse();
    }

    [Fact]
    public void A_backup_is_a_readable_copy_named_after_the_time()
    {
        CreateDatabaseWithRow("Heat");

        var backup = DatabaseBackup.Run(DatabasePath, BackupDirectory, _clock);

        Path.GetFileName(backup).ShouldBe("shelf-20261009-083000.db");
        ReadRows(backup!).ShouldBe(["Heat"]);
    }

    [Fact]
    public void Only_the_newest_backups_are_kept()
    {
        CreateDatabaseWithRow("Heat");

        for (var i = 0; i < 5; i++)
        {
            DatabaseBackup.Run(DatabasePath, BackupDirectory, _clock, keep: 3);
            _clock.Advance(TimeSpan.FromDays(1));
        }

        Directory.GetFiles(BackupDirectory).Select(Path.GetFileName).Order().ShouldBe(
            ["shelf-20261011-083000.db", "shelf-20261012-083000.db", "shelf-20261013-083000.db"]);
    }

    [Fact]
    public void Unrelated_files_in_the_backup_folder_are_left_alone()
    {
        CreateDatabaseWithRow("Heat");
        Directory.CreateDirectory(BackupDirectory);
        var note = Path.Combine(BackupDirectory, "readme.txt");
        File.WriteAllText(note, "mine");

        DatabaseBackup.Run(DatabasePath, BackupDirectory, _clock, keep: 1);
        _clock.Advance(TimeSpan.FromHours(1));
        DatabaseBackup.Run(DatabasePath, BackupDirectory, _clock, keep: 1);

        File.Exists(note).ShouldBeTrue();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateDatabaseWithRow(string title)
    {
        using var connection = Open(DatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Movies (Title TEXT); INSERT INTO Movies VALUES ($title);";
        command.Parameters.AddWithValue("$title", title);
        command.ExecuteNonQuery();
    }

    private static List<string> ReadRows(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title FROM Movies";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
