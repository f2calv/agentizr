namespace CasCap.Entities;

/// <summary>Immutable schema-versioned JSON snapshot of one tenant agent definition.</summary>
public sealed class AgentDefinitionSnapshotEntity
{
    /// <summary>Gets or sets the generated identifier.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the stable agent name.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the immutable definition version.</summary>
    public required string DefinitionVersion { get; set; }

    /// <summary>Gets or sets the JSON document schema version.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets the complete definition snapshot JSON.</summary>
    public required string DefinitionJson { get; set; }

    /// <summary>Gets or sets when the snapshot was published.</summary>
    public DateTimeOffset PublishedAtUtc { get; set; }

    /// <summary>Gets or sets the authenticated publisher identifier.</summary>
    public string? PublishedBy { get; set; }

    /// <summary>Gets or sets the optional publication reason.</summary>
    public string? ChangeReason { get; set; }
}
