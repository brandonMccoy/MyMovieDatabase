using System.Text.Json;
using MMDb.Contracts;

namespace MMDb.Testing;

internal static class Wire
{
    /// <summary>A contract as it goes over the wire, to compare two of them whatever list types they hold.</summary>
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, ShelfJson.Options);
}
