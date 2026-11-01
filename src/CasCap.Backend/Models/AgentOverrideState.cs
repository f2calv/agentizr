namespace CasCap.Models;

/// <summary>Per-session model, instruction, and persistence overrides.</summary>
public sealed record AgentOverrideState
{
    /// <summary>Gets whether conversation state should be loaded and persisted.</summary>
    public bool SessionEnabled { get; init; } = true;

    /// <summary>Gets the optional model override.</summary>
    public string? ModelName { get; init; }

    /// <summary>Gets the optional instruction override.</summary>
    public string? Instructions { get; init; }
}
