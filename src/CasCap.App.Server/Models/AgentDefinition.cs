namespace CasCap.Models;

/// <summary>A versioned agent and provider definition resolved for one tenant.</summary>
public sealed record AgentDefinition
{
    /// <summary>Gets the stable tenant-local agent key used by routes, storage, and tool references.</summary>
    [Required, MinLength(1), MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Gets the definition version.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public required string Version { get; init; }

    /// <summary>Gets the configured agent.</summary>
    public required AgentConfig Agent { get; init; }

    /// <summary>Gets the provider configuration without credentials.</summary>
    public required ProviderConfig Provider { get; init; }
}
