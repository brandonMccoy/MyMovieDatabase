namespace MMDb.Contracts.Sync;

/// <summary>Everything in the PC's collection and wishlist by key, for a phone to compare its copy with.</summary>
/// <param name="Schema">Names the PC database's tables as they are now. Rows only travel between databases on the same schema.</param>
public sealed record SyncManifest(string Schema, IReadOnlyList<SyncTable> Tables);

/// <param name="Rows">Each row's key with a stamp that changes whenever the row does.</param>
public sealed record SyncTable(string Name, IReadOnlyDictionary<string, string> Rows);

public sealed record SyncRowsRequest(string Table, IReadOnlyList<string> Keys);

public sealed record SyncRowsResponse(IReadOnlyList<SyncRow> Rows);

/// <summary>A row as the PC's database holds it.</summary>
/// <param name="Values">By column: text, a number or <see langword="null"/>.</param>
/// <param name="Parts">The rows that belong to it (a film's formats), column by column.</param>
public sealed record SyncRow(string Key, IReadOnlyDictionary<string, object?> Values, IReadOnlyList<IReadOnlyDictionary<string, object?>> Parts);
