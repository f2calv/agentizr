namespace CasCap.Models;

/// <summary>Internal projection of one definition activation or rollback event.</summary>
public sealed record AgentDefinitionActivationItem
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
