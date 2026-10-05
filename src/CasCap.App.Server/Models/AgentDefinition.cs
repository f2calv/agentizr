namespace CasCap.Models;

/// <summary>A versioned agent and provider definition resolved for one tenant.</summary>
public sealed record AgentDefinition
{
    /// <summary>Gets the definition version.</summary>
    [Required, MinLength(1)]
    public required string Version { get; init; }

    /// <summary>Gets the configured agent.</summary>
    public required AgentConfig Agent { get; init; }

    /// <summary>Gets the provider configuration without credentials.</summary>
    public required ProviderConfig Provider { get; init; }
}
