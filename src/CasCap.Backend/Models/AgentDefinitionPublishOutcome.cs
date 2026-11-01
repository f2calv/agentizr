namespace CasCap.Models;

/// <summary>Validation-aware result of publishing one immutable definition snapshot.</summary>
public sealed record AgentDefinitionPublishOutcome
{
    /// <summary>Gets the published snapshot on success.</summary>
    public AgentDefinitionSnapshotItem? Snapshot { get; init; }

    /// <summary>Gets a public-safe validation error.</summary>
    public string? ValidationError { get; init; }

    /// <summary>Gets whether the requested version already exists.</summary>
    public bool IsConflict { get; init; }

    /// <summary>Gets whether the current definition authority supports publication.</summary>
    public bool IsSupported { get; init; } = true;
}
