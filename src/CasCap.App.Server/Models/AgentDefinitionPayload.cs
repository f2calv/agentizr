namespace CasCap.Models;

/// <summary>Administrative payload whose identity and version come from the trusted request envelope.</summary>
public sealed record AgentDefinitionPayload
{
    /// <summary>Gets the configured agent behavior.</summary>
    public required AgentConfig Agent { get; init; }

    /// <summary>Gets secret-free provider infrastructure.</summary>
    public required ProviderConfig Provider { get; init; }
}
