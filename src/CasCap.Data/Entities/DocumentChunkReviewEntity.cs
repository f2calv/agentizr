namespace CasCap.Entities;

/// <summary>Append-only, authenticated selection decisions for an immutable review-required snapshot.</summary>
public sealed class DocumentChunkReviewEntity
{
    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the immutable snapshot identity.</summary>
    public required string GenerationId { get; set; }

    /// <summary>Gets or sets the monotonically increasing snapshot-local revision.</summary>
    public long Revision { get; set; }

    /// <summary>Gets or sets the server-authenticated reviewer, never caller-supplied attribution.</summary>
    public required string ActorId { get; set; }

    /// <summary>Gets or sets the canonical excluded ordinals and separate optional explanations.</summary>
    public required string ExclusionsJson { get; set; }

    /// <summary>Gets or sets the database-authoritative recording time.</summary>
    public DateTimeOffset RecordedAtUtc { get; set; }
}
