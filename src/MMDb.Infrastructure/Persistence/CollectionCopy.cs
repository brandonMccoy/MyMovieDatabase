using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace MMDb.Infrastructure.Persistence;

/// <summary>Which rows a table has, each with a stamp that changes whenever the row does.</summary>
/// <param name="Stamps">By the row's key.</param>
public sealed record CopiedTable(string Name, IReadOnlyDictionary<string, string> Stamps);

/// <summary>
/// A row as SQLite holds it, column by column: text, a whole number, a real number or <see langword="null"/>.
/// </summary>
/// <param name="Parts">The rows that belong to it and travel with it (a film's formats).</param>
public sealed record CopiedRow(string Key, IReadOnlyDictionary<string, object?> Values, IReadOnlyList<IReadOnlyDictionary<string, object?>> Parts);

/// <summary>
/// The collection and wishlist as rows, for a phone's copy of the PC's database: the PC lists and reads them,
/// the phone writes them into its own. Rows travel as they are, so both databases must have the same tables
/// (see <see cref="SchemaAsync"/>). Each call uses its own context.
/// </summary>
public sealed class CollectionCopy(DbContextOptions<ShelfDbContext> options)
{
    // A cover arrives after the save without a new version, so it is part of the stamp.
    private const string VersionAndCover = "\"Version\" || '|' || coalesce(\"CoverImage\", '')";

    // SQLite takes a few hundred parameters in its stride; callers may pass any number of keys.
    private const int KeysPerQuery = 400;

    private static readonly Table[] Copied =
    [
        new("Movies", "Id", VersionAndCover, Parts: "MovieFormats", PartsKey: "MovieId", HasCover: true),
        new("Comics", "Id", VersionAndCover, HasCover: true),
        new("ComicSeries", "MetronId", "\"FetchedAt\"", NumericKey: true),
        new("WishlistMovies", "Id", VersionAndCover, HasCover: true),
        new("WishlistComics", "Id", VersionAndCover, HasCover: true),
    ];

    /// <summary>The tables that are copied, in the order to write them.</summary>
    public static IReadOnlyList<string> Tables { get; } = [.. Copied.Select(t => t.Name)];

    /// <summary>Names the database's tables as they are now: the newest migration applied.</summary>
    public async Task<string> SchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        return (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault() ?? "";
    }

    /// <summary>Every copied row's key and stamp. Reads no more than that, so it is cheap to ask often.</summary>
    public async Task<IReadOnlyList<CopiedTable>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        var connection = await OpenAsync(db, cancellationToken);
        var tables = new List<CopiedTable>();
        foreach (var table in Copied)
        {
            var stamps = new Dictionary<string, string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"{table.Key}\", {table.Stamp} FROM \"{table.Name}\"";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                stamps[Text(reader.GetValue(0))] = Text(reader.GetValue(1));
            }

            tables.Add(new(table.Name, stamps));
        }

        return tables;
    }

    /// <summary>The rows with these keys, whole. Keys the table doesn't have are left out.</summary>
    public async Task<IReadOnlyList<CopiedRow>> ReadAsync(string table, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var copied = Find(table);
        await using var db = new ShelfDbContext(options);
        var connection = await OpenAsync(db, cancellationToken);
        var rows = new List<CopiedRow>();
        foreach (var chunk in keys.Chunk(KeysPerQuery))
        {
            var parts = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>();
            if (copied.Parts is not null)
            {
                await foreach (var part in SelectAsync(connection, copied.Parts, copied.PartsKey!, copied, chunk, cancellationToken))
                {
                    var owner = Text(part[copied.PartsKey!]);
                    (parts.TryGetValue(owner, out var list) ? list : parts[owner] = []).Add(part);
                }
            }

            await foreach (var row in SelectAsync(connection, copied.Name, copied.Key, copied, chunk, cancellationToken))
            {
                var key = Text(row[copied.Key]);
                rows.Add(new(key, row, parts.TryGetValue(key, out var mine) ? mine : []));
            }
        }

        return rows;
    }

    /// <summary>
    /// Writes rows read from another database into this one: rows it doesn't have are added, rows it has are
    /// replaced, parts and all. All of them or, if one can't be written, none.
    /// </summary>
    /// <exception cref="InvalidOperationException">A row has a column this database doesn't: the two aren't on the same schema.</exception>
    public async Task WriteAsync(string table, IReadOnlyCollection<CopiedRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var copied = Find(table);
        await using var db = new ShelfDbContext(options);
        var connection = await OpenAsync(db, cancellationToken);
        var columns = await ColumnsAsync(connection, copied.Name, cancellationToken);
        var partColumns = copied.Parts is null ? [] : await ColumnsAsync(connection, copied.Parts, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (copied.Parts is not null)
            {
                await ExecuteAsync(connection, transaction, $"DELETE FROM \"{copied.Parts}\" WHERE \"{copied.PartsKey}\" = @p0", [KeyValue(copied, row.Key)], cancellationToken);
            }

            await InsertAsync(connection, transaction, copied.Name, columns, row.Values, cancellationToken);
            foreach (var part in row.Parts)
            {
                await InsertAsync(connection, transaction, copied.Parts!, partColumns, part, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Removes the rows with these keys, parts and all.</summary>
    /// <returns>How many rows there were to remove.</returns>
    public async Task<int> RemoveAsync(string table, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var copied = Find(table);
        await using var db = new ShelfDbContext(options);
        var connection = await OpenAsync(db, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var removed = 0;
        foreach (var chunk in keys.Chunk(KeysPerQuery))
        {
            var values = chunk.Select(key => KeyValue(copied, key)).ToArray();
            var list = string.Join(", ", values.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)));
            if (copied.Parts is not null)
            {
                await ExecuteAsync(connection, transaction, $"DELETE FROM \"{copied.Parts}\" WHERE \"{copied.PartsKey}\" IN ({list})", values, cancellationToken);
            }

            removed += await ExecuteAsync(connection, transaction, $"DELETE FROM \"{copied.Name}\" WHERE \"{copied.Key}\" IN ({list})", values, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    /// <summary>The file name of every cover the copied rows show, each once.</summary>
    public async Task<IReadOnlyList<string>> CoverNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ShelfDbContext(options);
        var connection = await OpenAsync(db, cancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in Copied.Where(t => t.HasCover))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"CoverImage\" FROM \"{table.Name}\" WHERE \"CoverImage\" IS NOT NULL";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }
        }

        return [.. names];
    }

    private static Table Find(string table) =>
        Copied.FirstOrDefault(t => t.Name == table) ?? throw new ArgumentException($"\"{table}\" isn't a table that is copied.", nameof(table));

    private static object KeyValue(Table table, string key) =>
        table.NumericKey ? long.Parse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : key;

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static async Task<DbConnection> OpenAsync(ShelfDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return db.Database.GetDbConnection();
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> SelectAsync(
        DbConnection connection,
        string from,
        string keyColumn,
        Table table,
        string[] keys,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var list = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + list.Count.ToString(CultureInfo.InvariantCulture);
            parameter.Value = KeyValue(table, key);
            command.Parameters.Add(parameter);
            list.Add(parameter.ParameterName);
        }

        command.CommandText = $"SELECT * FROM \"{from}\" WHERE \"{keyColumn}\" IN ({string.Join(", ", list)})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            yield return row;
        }
    }

    private static async Task<HashSet<string>> ColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    // Column names come from the other database; only ones this table has are ever put into SQL.
    private static async Task InsertAsync(
        DbConnection connection, DbTransaction transaction, string table, HashSet<string> columns, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        if (values.Keys.FirstOrDefault(name => !columns.Contains(name)) is { } unknown)
        {
            throw new InvalidOperationException($"This database's {table} has no column \"{unknown}\".");
        }

        var names = values.Keys.ToArray();
        await ExecuteAsync(
            connection,
            transaction,
            $"INSERT OR REPLACE INTO \"{table}\" ({string.Join(", ", names.Select(n => $"\"{n}\""))}) VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)))})",
            [.. names.Select(n => values[n])],
            cancellationToken);
    }

    private static async Task<int> ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, object?[] values, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(CultureInfo.InvariantCulture);
            parameter.Value = values[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record Table(string Name, string Key, string Stamp, string? Parts = null, string? PartsKey = null, bool NumericKey = false, bool HasCover = false);
}
