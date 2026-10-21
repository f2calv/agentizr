namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports the instructions the next run of one agent session will use.</summary>
public sealed record AgentInstructionsResponse
{
    /// <summary>Gets the active definition version the instructions belong to.</summary>
    /// <example>2026-10-16.1</example>
    [Required, MaxLength(200)]
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets whether a per-session override replaces the definition instructions.</summary>
    /// <example>false</example>
    public bool Overridden { get; init; }

    /// <summary>Gets the effective instructions.</summary>
    /// <example>You are a patient German tutor.</example>
    [Required, MaxLength(100_000)]
    public required string Instructions { get; init; }
}
