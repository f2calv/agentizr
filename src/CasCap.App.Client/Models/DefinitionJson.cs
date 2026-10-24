using System.Text.Json;

namespace CasCap.Client.Models;

/// <summary>Reads display values from a definition snapshot's JSON without depending on server model types.</summary>
public static class DefinitionJson
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>Gets a scalar property of the definition's <c>agent</c> or <c>provider</c> object as text.</summary>
    /// <param name="definition">The snapshot definition.</param>
    /// <param name="section">Either <c>agent</c> or <c>provider</c>.</param>
    /// <param name="property">The camel-case property name.</param>
    public static string? GetText(JsonElement definition, string section, string property) =>
        definition.ValueKind == JsonValueKind.Object
            && definition.TryGetProperty(section, out var sectionElement)
            && sectionElement.ValueKind == JsonValueKind.Object
            && sectionElement.TryGetProperty(property, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => value.GetRawText(),
                }
                : null;

    /// <summary>Gets the agent's tool sources as indented JSON.</summary>
    public static string GetToolsJson(JsonElement definition) =>
        definition.ValueKind == JsonValueKind.Object
            && definition.TryGetProperty("agent", out var agent)
            && agent.TryGetProperty("tools", out var tools)
                ? JsonSerializer.Serialize(tools, IndentedOptions)
                : "[]";
}
