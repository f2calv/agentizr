namespace CasCap.Entities;

/// <summary>Write-once paid intent and bounded response metadata; unknown submissions are never automatically replayed.</summary>
public sealed class DocumentEmbeddingBatchEntity
{
    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the indexing job.</summary>
    public required string JobId { get; set; }

    /// <summary>Gets or sets the zero-based batch ordinal.</summary>
    public int Ordinal { get; set; }

    /// <summary>Gets or sets the submitting database claim.</summary>
    public Guid ClaimId { get; set; }

    /// <summary>Gets or sets the exact ordered input and claim digest.</summary>
    public required string InputSha256 { get; set; }

    /// <summary>Gets or sets submission/receipt state; unresolved submissions are never replayed.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets native usage when a valid receipt was received.</summary>
    public long? InputTokens { get; set; }

    /// <summary>Gets or sets the write-once response identity.</summary>
    public string? ResultSha256 { get; set; }

    /// <summary>Gets or sets the time intent was durably reserved before transport.</summary>
    public DateTimeOffset SubmittedAtUtc { get; set; }

    /// <summary>Gets or sets valid receipt time, including late receipts that cannot publish.</summary>
    public DateTimeOffset? ReceivedAtUtc { get; set; }
}
