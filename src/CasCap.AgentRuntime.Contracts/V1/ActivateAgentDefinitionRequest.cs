namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Activates an existing immutable agent definition snapshot.</summary>
public sealed record ActivateAgentDefinitionRequest
{
    /// <summary>Gets the operator-supplied activation or rollback reason.</summary>
    /// <example>Rollback after provider regression.</example>
    [MaxLength(2_000)]
    public string? ChangeReason { get; init; }

    /// <summary>
    /// Gets an optional evaluation that must pass before the version is activated. The server may require one.
    /// </summary>
    public EvaluateAgentDefinitionRequest? Evaluation { get; init; }
}
