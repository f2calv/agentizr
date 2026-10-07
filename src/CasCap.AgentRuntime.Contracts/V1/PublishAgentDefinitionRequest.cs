namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Publishes one immutable schema-versioned agent definition document.</summary>
public sealed record PublishAgentDefinitionRequest
{
    /// <summary>Gets the immutable tenant-local definition version.</summary>
    /// <example>2026-10-07.1</example>
    [Required, MinLength(1), MaxLength(100)]
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets the JSON document schema version.</summary>
    /// <example>1</example>
    [JsonRequired, Range(1, int.MaxValue)]
    public int SchemaVersion { get; init; }

    /// <summary>Gets the agent/provider payload without tenant identity, credentials, name or version.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>Gets the operator-supplied publication reason.</summary>
    /// <example>Add authenticated remote MCP tools.</example>
    [MaxLength(2_000)]
    public string? ChangeReason { get; init; }
}
