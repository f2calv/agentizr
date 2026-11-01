namespace CasCap.Models;

/// <summary>Internal projection of one immutable definition snapshot.</summary>
public sealed record AgentDefinitionSnapshotItem
{
    /// <summary>Gets the complete secret-free definition.</summary>
    public required AgentDefinition Definition { get; init; }

    /// <summary>Gets the JSON document schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets when publication occurred.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the authenticated publisher identifier.</summary>
    public string? PublishedBy { get; init; }

    /// <summary>Gets the publication reason.</summary>
    public string? ChangeReason { get; init; }

    /// <summary>Gets whether this snapshot is active.</summary>
    public bool IsActive { get; init; }
}
