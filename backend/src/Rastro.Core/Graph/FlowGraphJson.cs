using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rastro.Core.Graph;

// Formato JSON único do grafo (CLI, API, MCP): camelCase, enums como int, nulos omitidos.
public static class FlowGraphJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string Serialize(FlowGraph graph) => JsonSerializer.Serialize(graph, Options);

    public static FlowGraph Deserialize(string json) =>
        JsonSerializer.Deserialize<FlowGraph>(json, Options)
        ?? throw new JsonException("JSON vazio não é um grafo.");
}
