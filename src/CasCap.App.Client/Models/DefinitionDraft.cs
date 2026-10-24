using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.Client.Models;

/// <summary>
/// Builds a publication payload from an existing version or from scratch, changing only edited fields so that
/// supported fields without a dedicated control are preserved.
/// </summary>
public sealed class DefinitionDraft
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    private readonly JsonObject _agent;
    private readonly JsonObject _provider;

    private DefinitionDraft(JsonObject agent, JsonObject provider)
    {
        _agent = agent;
        _provider = provider;
    }

    /// <summary>Starts a draft from a published snapshot definition.</summary>
    /// <exception cref="FormatException">The definition has no <c>agent</c> or <c>provider</c> object.</exception>
    public static DefinitionDraft FromDefinition(JsonElement definition) =>
        JsonNode.Parse(definition.GetRawText()) is JsonObject root
            && root["agent"] is JsonObject agent
            && root["provider"] is JsonObject provider
                ? new DefinitionDraft((JsonObject)agent.DeepClone(), (JsonObject)provider.DeepClone())
                : throw new FormatException("The definition has no agent or provider object.");

    /// <summary>Starts an empty draft for a new agent.</summary>
    public static DefinitionDraft Empty() => new(new JsonObject { ["enabled"] = true }, []);

    /// <summary>Copies the draft into form fields for the given agent.</summary>
    public DefinitionForm ToForm(string agentName) => new()
    {
        AgentName = agentName,
        DisplayName = Text(_agent, "name") ?? agentName,
        Description = Text(_agent, "description") ?? string.Empty,
        Prompt = Text(_agent, "prompt") ?? string.Empty,
        Instructions = Text(_agent, "instructions") ?? string.Empty,
        ProviderName = Text(_agent, "provider") ?? string.Empty,
        ProviderType = Text(_provider, "type") ?? string.Empty,
        Endpoint = Text(_provider, "endpoint"),
        ModelName = Text(_provider, "modelName") ?? string.Empty,
        ReasoningEffort = Text(_provider, "reasoningEffort"),
        ToolsJson = _agent["tools"]?.ToJsonString(IndentedOptions) ?? "[]",
    };

    /// <summary>Applies the form to a copy of the draft and returns the <c>{ agent, provider }</c> publication payload.</summary>
    /// <param name="form">The edited fields.</param>
    /// <param name="payload">The payload when the form is well formed.</param>
    /// <param name="error">Why the payload could not be built.</param>
    public bool TryCreatePayload(DefinitionForm form, out JsonElement payload, out string? error)
    {
        payload = default;
        JsonNode? tools;
        try
        {
            tools = JsonNode.Parse(form.ToolsJson);
        }
        catch (JsonException ex)
        {
            error = $"Tools must be a JSON array: {ex.Message}";
            return false;
        }
        if (tools is not JsonArray)
        {
            error = "Tools must be a JSON array.";
            return false;
        }

        var agent = (JsonObject)_agent.DeepClone();
        agent["name"] = form.DisplayName.Trim();
        agent["description"] = form.Description;
        agent["prompt"] = form.Prompt;
        agent["instructions"] = form.Instructions;
        agent["provider"] = form.ProviderName.Trim();
        agent["tools"] = tools;
        agent.TryAdd("prompts", new JsonArray());

        var provider = (JsonObject)_provider.DeepClone();
        provider.Remove("apiKey");
        provider["type"] = form.ProviderType;
        provider["endpoint"] = string.IsNullOrWhiteSpace(form.Endpoint) ? null : form.Endpoint.Trim();
        provider["modelName"] = form.ModelName.Trim();
        provider["reasoningEffort"] = string.IsNullOrWhiteSpace(form.ReasoningEffort) ? null : form.ReasoningEffort;

        payload = JsonSerializer.SerializeToElement(new JsonObject { ["agent"] = agent, ["provider"] = provider });
        error = null;
        return true;
    }

    private static string? Text(JsonObject source, string property) =>
        source[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : source[property]?.ToJsonString();
}
