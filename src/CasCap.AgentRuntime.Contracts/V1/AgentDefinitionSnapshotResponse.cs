namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Returns one immutable definition snapshot and publication metadata.</summary>
public sealed record AgentDefinitionSnapshotResponse
{
    /// <summary>Gets the stable tenant-local agent name.</summary>
    /// <example>CommsAgent</example>
    public required string AgentName { get; init; }

    /// <summary>Gets the immutable definition version.</summary>
    /// <example>2026-10-07.1</example>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets the JSON document schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the complete secret-free definition document.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>Gets when the snapshot was published.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets the authenticated publisher identifier.</summary>
    public string? PublishedBy { get; init; }

    /// <summary>Gets the publication reason.</summary>
    public string? ChangeReason { get; init; }

    /// <summary>Gets whether this snapshot is currently active.</summary>
    public bool IsActive { get; init; }
}
