namespace CasCap.Entities;

/// <summary>Points one tenant agent name at its active immutable definition snapshot.</summary>
public sealed class ActiveAgentDefinitionEntity
{
    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the stable agent name.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the active snapshot identifier.</summary>
    public long SnapshotId { get; set; }

    /// <summary>Gets or sets when the pointer was last changed.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets the active snapshot navigation.</summary>
    public AgentDefinitionSnapshotEntity? Snapshot { get; set; }
}
