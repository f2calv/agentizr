namespace CasCap.Client.Models;

/// <summary>Activates or rolls back to an immutable version only if the active version is still as expected.</summary>
public sealed record AdministrationActivationRequest
{
    /// <summary>Gets the operator-supplied activation or rollback reason.</summary>
    /// <example>Roll back after a provider regression.</example>
    [Required, MinLength(1), MaxLength(2_000)]
    public required string ChangeReason { get; init; }

    /// <summary>
    /// Gets the version the administrator saw as active, or <see langword="null" /> when no version was active.
    /// </summary>
    /// <remarks>The server refuses the activation with a conflict when another decision changed it since.</remarks>
    /// <example>2026-10-07.1</example>
    [MaxLength(100)]
    public string? ExpectedActiveVersion { get; init; }

    /// <summary>Gets an optional evaluation that must pass before the version is activated.</summary>
    public EvaluateAgentDefinitionRequest? Evaluation { get; init; }
}
