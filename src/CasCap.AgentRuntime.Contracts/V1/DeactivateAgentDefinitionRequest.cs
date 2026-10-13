namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Deactivates an agent's active definition, leaving it with no active version.</summary>
public sealed record DeactivateAgentDefinitionRequest
{
    /// <summary>Gets the operator-supplied reason recorded in the activation history.</summary>
    /// <example>Retire the unreachable bootstrap definition.</example>
    [Required, MinLength(1), MaxLength(2_000)]
    public required string ChangeReason { get; init; }
}
