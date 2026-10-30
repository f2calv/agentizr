namespace CasCap.Entities;

/// <summary>Durable, explicitly authorized indexing generation and exact input/budget bindings.</summary>
public sealed class DocumentIndexingJobEntity
{
    /// <summary>Gets or sets the deterministic tenant/manifest generation identity.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the owning collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the source document identity.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the immutable preparation receipt identity.</summary>
    public required string PreparationJobId { get; set; }

    /// <summary>Gets or sets the reviewed Markdown generation.</summary>
    public required string GenerationId { get; set; }

    /// <summary>Gets or sets the exact approved input and policy digest.</summary>
    public required string ManifestSha256 { get; set; }

    /// <summary>Gets or sets private inputs; cleared on source retirement while audit metadata remains.</summary>
    public string? ManifestJson { get; set; }

    /// <summary>Gets or sets the complete policy/profile digest.</summary>
    public required string PolicySha256 { get; set; }

    /// <summary>Gets or sets the monotonically accepted collection policy revision.</summary>
    public int PolicyRevision { get; set; }

    /// <summary>Gets or sets the server-authenticated authorizing actor; never returned to clients.</summary>
    public required string ActorId { get; set; }

    /// <summary>Gets or sets the durable lifecycle state.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets the explicit optimistic publication predecessor.</summary>
    public string? ExpectedActiveJobId { get; set; }

    /// <summary>Gets or sets the authorized logical submission ceiling.</summary>
    public int MaximumRequests { get; set; }

    /// <summary>Gets or sets the input-token allowance, not a financial guarantee.</summary>
    public int MaximumInputTokens { get; set; }

    /// <summary>Gets or sets the number of committed paid intents, including unknown outcomes.</summary>
    public int SubmittedRequests { get; set; }

    /// <summary>Gets or sets cumulative native received input-token usage.</summary>
    public long ReceivedInputTokens { get; set; }

    /// <summary>Gets or sets the number of atomically staged batches.</summary>
    public int CompletedBatches { get; set; }

    /// <summary>Gets or sets the committed staged vector count.</summary>
    public int VectorCount { get; set; }

    /// <summary>Gets or sets the current database claim.</summary>
    public Guid? ClaimId { get; set; }

    /// <summary>Gets or sets the database-time claim deadline.</summary>
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }

    /// <summary>Gets or sets durable acceptance time.</summary>
    public DateTimeOffset AcceptedAtUtc { get; set; }

    /// <summary>Gets or sets the terminal transition time.</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }
}
