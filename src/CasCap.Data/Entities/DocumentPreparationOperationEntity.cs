namespace CasCap.Entities;

/// <summary>Durable single-submission intent, receipt, candidate and usage for one tenant/job/stage/page.</summary>
public sealed class DocumentPreparationOperationEntity
{
    /// <summary>Gets or sets the deterministic operation identity.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning ingestion receipt.</summary>
    public required string JobId { get; set; }

    /// <summary>Gets or sets the tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the tenant-local collection.</summary>
    public required string CollectionId { get; set; }

    /// <summary>Gets or sets the immutable source document.</summary>
    public required string DocumentId { get; set; }

    /// <summary>Gets or sets the conditionally downloaded original-byte digest.</summary>
    public required string SourceSha256 { get; set; }

    /// <summary>Gets or sets the admission-plan digest, including definition, model revision and budgets.</summary>
    public required string PlanSha256 { get; set; }

    /// <summary>Gets or sets the logical request digest, including derived page and managed-draft provenance.</summary>
    public required string RequestSha256 { get; set; }

    /// <summary>Gets or sets the first submitted derived PDF's digest; writer metadata is not a stable replay identity.</summary>
    public string? DerivedPdfSha256 { get; set; }

    /// <summary>Gets or sets the service stage.</summary>
    public required string Stage { get; set; }

    /// <summary>Gets or sets the physical page; zero denotes the managed whole-document request.</summary>
    public int PageNumber { get; set; }

    /// <summary>Gets or sets the claim that committed the unique submission intent.</summary>
    public Guid SubmissionClaimId { get; set; }

    /// <summary>Gets or sets the receipt state.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets the credential-free managed operation location, never a signed URL.</summary>
    public string? OperationUri { get; set; }

    /// <summary>Gets or sets bounded explicit candidate/response projections, never raw SDK objects or credentials.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Gets or sets the original response-projection digest, unaffected by JSONB formatting.</summary>
    public string? ResultSha256 { get; set; }

    /// <summary>Gets or sets reported token/analyzed-page units; absent values remain unknown rather than zero.</summary>
    public string? UsageJson { get; set; }

    /// <summary>Gets or sets the durable intent time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets the durable receipt time.</summary>
    public DateTimeOffset? ReceivedAtUtc { get; set; }
}
