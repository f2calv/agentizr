namespace CasCap.Entities;

/// <summary>Latest source notification and durable admission status for one tenant-owned blob.</summary>
public sealed class KnowledgeDocumentEntity
{
    /// <summary>Gets or sets the opaque tenant/collection/source digest.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning logical collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the configured source blob URI.</summary>
    public required string BlobUri { get; set; }

    /// <summary>Gets or sets the normalized per-blob ordering token.</summary>
    public required string Sequencer { get; set; }

    /// <summary>Gets or sets the ETag reported by the latest source notification.</summary>
    public string? ETag { get; set; }

    /// <summary>Gets or sets the durable admission state.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets when the latest state was admitted.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets the last complete-run inventory observation; null denotes event-only admission.</summary>
    public Guid? LastSeenSyncId { get; set; }
}
