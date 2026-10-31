namespace CasCap.Entities;

/// <summary>Immutable review-required Markdown output; never approval, embedding eligibility or searchable activation.</summary>
public sealed class DocumentMarkdownGenerationEntity
{
    /// <summary>Gets or sets the source-qualified snapshot digest.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the source document identity.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the source preparation job.</summary>
    public required string JobId { get; set; }

    /// <summary>Gets or sets the admitted source ordering token.</summary>
    public required string Sequencer { get; set; }

    /// <summary>Gets or sets the conditionally downloaded source revision.</summary>
    public required string ETag { get; set; }

    /// <summary>Gets or sets the source/profile/review projection identity.</summary>
    public required string ProjectionId { get; set; }

    /// <summary>Gets or sets the versioned candidate, receipt-reference and chunk snapshot.</summary>
    public required string PayloadJson { get; set; }

    /// <summary>Gets or sets the database-authoritative storage time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
