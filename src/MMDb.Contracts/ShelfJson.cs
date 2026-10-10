using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMDb.Contracts;

/// <summary>How the PC and the phone write the contracts as JSON: camelCase, enums by name.</summary>
public static class ShelfJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
