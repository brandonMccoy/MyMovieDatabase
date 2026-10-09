using Microsoft.Data.Sqlite;

namespace MMDb.Testing;

/// <summary>
/// A private in-memory SQLite database that lives as long as this object. Every connection opened with
/// <see cref="ConnectionString"/> sees the same data, so each test step can use a fresh DbContext.
/// </summary>
internal sealed class InMemorySqlite : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public InMemorySqlite()
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "shelf-test-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public void Dispose() => _keepAlive.Dispose();
}
