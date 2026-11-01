namespace CasCap.Entities;

/// <summary>Atomically published generation pointer; readers must additionally check current eligibility.</summary>
public sealed class ActiveDocumentIndexEntity
{
    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the source document.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the atomically selected complete indexing generation.</summary>
    public required string JobId { get; set; }

    /// <summary>Gets or sets the database publication time.</summary>
    public DateTimeOffset ActivatedAtUtc { get; set; }
}
