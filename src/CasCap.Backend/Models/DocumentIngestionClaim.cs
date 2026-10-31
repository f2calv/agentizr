namespace CasCap.Models;

/// <summary>An immutable source revision and attempt identity; not an external-write fencing token.</summary>
internal sealed record DocumentIngestionClaim
{
    /// <summary>Gets the durable job receipt identity.</summary>
    public required string JobId { get; init; }

    /// <summary>Gets the current attempt identity.</summary>
    public required Guid ClaimId { get; init; }

    /// <summary>Gets the tenant-qualified document identity.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Gets the authorized tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the authorized collection.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the admitted source ordering token.</summary>
    public required string Sequencer { get; init; }

    /// <summary>Gets the canonical unsigned source location.</summary>
    public required string BlobUri { get; init; }

    /// <summary>Gets the admitted entity tag for a later conditional download.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the database deadline after which job outcomes are rejected.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Gets the admission-pinned hybrid plan; old/local-only jobs retain null.</summary>
    public string? PreparationPlanJson { get; init; }
}
