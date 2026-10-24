namespace CasCap.Client.Models;

/// <summary>Deactivates the active definition only if it is still the version the administrator saw.</summary>
public sealed record AdministrationDeactivationRequest
{
    /// <summary>Gets the operator-supplied reason recorded in the activation history.</summary>
    /// <example>Retire the unused assistant.</example>
    [Required, MinLength(1), MaxLength(2_000)]
    public required string ChangeReason { get; init; }

    /// <summary>Gets the version the administrator saw as active.</summary>
    /// <example>2026-10-07.1</example>
    [Required, MinLength(1), MaxLength(100)]
    public required string ExpectedActiveVersion { get; init; }
}
