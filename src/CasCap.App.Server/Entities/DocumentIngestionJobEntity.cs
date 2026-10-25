namespace CasCap.Entities;

/// <summary>An idempotent event receipt and queued processing intent, committed with document state.</summary>
public sealed class DocumentIngestionJobEntity
{
    /// <summary>Gets or sets the digest of the source topic and event ID.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets a digest detecting reuse of an event ID with different data.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Gets or sets the tenant-qualified source document digest.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the event's normalized ordering token.</summary>
    public required string Sequencer { get; set; }

    /// <summary>Gets or sets durable admission or processing status.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets the durable acceptance time.</summary>
    public DateTimeOffset AcceptedAtUtc { get; set; }

    /// <summary>Gets or sets the number of claimed attempts, including abandoned work.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets the current attempt identity.</summary>
    public Guid? ClaimId { get; set; }

    /// <summary>Gets or sets the current attempt's database-enforced deadline.</summary>
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the earliest allowed retry time.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Gets or sets the terminal processing time, excluding ignored admission receipts.</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>Gets or sets a bounded content-free failure code, never an exception message.</summary>
    public string? LastFailureCode { get; set; }
}
