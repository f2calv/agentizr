namespace CasCap.Models;

/// <summary>Result of activating an immutable definition version.</summary>
public sealed record AgentDefinitionActivationOutcome
{
    /// <summary>Gets whether the version was activated.</summary>
    public bool Activated { get; init; }

    /// <summary>Gets whether the current definition authority supports activation.</summary>
    public bool IsSupported { get; init; } = true;

    /// <summary>Gets a public-safe activation validation error.</summary>
    public string? ValidationError { get; init; }
}
