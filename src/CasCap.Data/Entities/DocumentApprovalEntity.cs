namespace CasCap.Entities;

/// <summary>Append-only readiness assessments bound to one tenant, snapshot and chunk-review revision.</summary>
public sealed class DocumentApprovalEntity
{
    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the immutable snapshot identity.</summary>
    public required string GenerationId { get; set; }

    /// <summary>Gets or sets the monotonically increasing assessment revision.</summary>
    public long Revision { get; set; }

    /// <summary>Gets or sets the assessed saved selection revision.</summary>
    public long ChunkReviewRevision { get; set; }

    /// <summary>Gets or sets the authenticated reviewer, never caller attribution.</summary>
    public required string ActorId { get; set; }

    /// <summary>Gets or sets canonical classification and attestations, separate from embedding text.</summary>
    public required string AssessmentJson { get; set; }

    /// <summary>Gets or sets the database-authoritative recording time.</summary>
    public DateTimeOffset RecordedAtUtc { get; set; }
}
