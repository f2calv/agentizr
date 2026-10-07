namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports one definition activation or rollback event.</summary>
public sealed record AgentDefinitionActivationResponse
{
    /// <summary>Gets the activated definition version.</summary>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets when activation occurred.</summary>
    public DateTimeOffset ActivatedAtUtc { get; init; }

    /// <summary>Gets the authenticated actor identifier.</summary>
    public string? ActivatedBy { get; init; }

    /// <summary>Gets the activation or rollback reason.</summary>
    public string? ChangeReason { get; init; }
}
