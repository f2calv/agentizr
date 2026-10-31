using System.Text.Json.Nodes;

namespace CasCap.AgentRuntime.Evaluation.Tests.Models;

/// <summary>
/// Named description and parameter-schema overrides used by offline tool-surface assertions.
/// </summary>
/// <remarks>
/// Overrides change only the model-facing schema and description, never the wrapped tool's behavior.
/// </remarks>
internal sealed record ToolSurfaceVariant
{
    /// <summary>The variant evaluated as the reference point for every scenario.</summary>
    public static ToolSurfaceVariant Baseline { get; } = new() { Name = "baseline" };

    /// <summary>Short, unique name used in reports.</summary>
    public required string Name { get; init; }

    /// <summary>Replacement descriptions keyed by model-facing tool name, for example <c>get_order_status</c>.</summary>
    public IReadOnlyDictionary<string, string> DescriptionOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Replacement parameter descriptions keyed by <c>tool_name.parameterName</c>, for example
    /// <c>get_order_status.orderId</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ParameterDescriptionOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>Returns the description override for a tool, or <see langword="null"/> to keep the shipped one.</summary>
    /// <param name="toolName">The model-facing tool name.</param>
    public string? GetDescription(string toolName) =>
        DescriptionOverrides.TryGetValue(toolName, out var description) ? description : null;

    /// <summary>
    /// Returns the tool's parameter schema with any <see cref="ParameterDescriptionOverrides"/> applied,
    /// or <see langword="null"/> when the variant leaves the schema unchanged.
    /// </summary>
    /// <param name="toolName">The model-facing tool name.</param>
    /// <param name="schema">The shipped JSON schema.</param>
    /// <exception cref="InvalidOperationException">An override names a parameter the tool does not have.</exception>
    public JsonElement? RewriteSchema(string toolName, JsonElement schema)
    {
        var prefix = $"{toolName}.";
        var overrides = ParameterDescriptionOverrides
            .Where(o => o.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (overrides.Count == 0)
            return null;

        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = root["properties"]?.AsObject()
            ?? throw new InvalidOperationException($"Tool '{toolName}' has no parameters to describe.");
        foreach (var (key, description) in overrides)
        {
            var parameter = key[prefix.Length..];
            var property = properties[parameter]?.AsObject()
                ?? throw new InvalidOperationException($"Tool '{toolName}' has no parameter '{parameter}'.");
            property["description"] = description;
        }

        return JsonSerializer.SerializeToElement(root);
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
