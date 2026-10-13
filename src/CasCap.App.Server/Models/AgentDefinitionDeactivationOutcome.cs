namespace CasCap.Models;

/// <summary>Result of deactivating an agent's active definition.</summary>
public sealed record AgentDefinitionDeactivationOutcome
{
    /// <summary>Gets whether an active definition was deactivated.</summary>
    public bool Deactivated { get; init; }

    /// <summary>Gets whether the current definition authority supports deactivation.</summary>
    public bool IsSupported { get; init; } = true;

    /// <summary>Gets a public-safe deactivation validation error.</summary>
    public string? ValidationError { get; init; }
}
