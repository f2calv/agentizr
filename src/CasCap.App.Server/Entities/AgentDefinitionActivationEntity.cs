namespace CasCap.Entities;

/// <summary>Append-only audit record for definition activation and rollback.</summary>
public sealed class AgentDefinitionActivationEntity
{
    /// <summary>Gets or sets the generated identifier.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the stable agent name.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the activated snapshot identifier.</summary>
    public long SnapshotId { get; set; }

    /// <summary>Gets or sets when activation occurred.</summary>
    public DateTimeOffset ActivatedAtUtc { get; set; }

    /// <summary>Gets or sets the authenticated actor identifier.</summary>
    public string? ActivatedBy { get; set; }

    /// <summary>Gets or sets the activation or rollback reason.</summary>
    public string? ChangeReason { get; set; }

    /// <summary>Gets or sets the activated snapshot navigation.</summary>
    public AgentDefinitionSnapshotEntity? Snapshot { get; set; }
}
