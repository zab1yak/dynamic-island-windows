using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core.Storage;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for every persisted document.</summary>
public static class JsonDefaults
{
    /// <summary>Indented camelCase JSON, enums as strings, non-ASCII text kept readable.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
