namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Summarizes one immutable definition snapshot in newest-first history.</summary>
public sealed record AgentDefinitionHistoryItemResponse
{
    /// <summary>Gets the immutable definition version.</summary>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets the JSON document schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets when the snapshot was published.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the authenticated publisher identifier.</summary>
    public string? PublishedBy { get; init; }

    /// <summary>Gets the publication reason.</summary>
    public string? ChangeReason { get; init; }

    /// <summary>Gets whether this snapshot is currently active.</summary>
    public bool IsActive { get; init; }
}
