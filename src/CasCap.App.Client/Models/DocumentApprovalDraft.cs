namespace CasCap.Client.Models;

/// <summary>Mutable form state kept separate from immutable wire contracts and saved classifications.</summary>
public sealed class DocumentApprovalDraft(DocumentReviewGenerationResponse review) : IValidatableObject
{
    /// <summary>Gets or sets the reviewed language code.</summary>
    public string Language { get; set; } = review.Approval?.Assessment.Language ?? string.Empty;

    /// <summary>Gets or sets the explicitly selected document classification.</summary>
    public string DocumentType { get; set; } = review.Approval?.Assessment.DocumentType ?? string.Empty;

    /// <summary>Gets or sets the optional edition.</summary>
    public string? Edition { get; set; } = review.Approval?.Assessment.Edition;

    /// <summary>Gets or sets the optional highest-content CEFR classification.</summary>
    public string? CefrLevel { get; set; } = review.Approval?.Assessment.CefrLevel;

    /// <summary>Gets or sets whether to request readiness approval rather than withhold it.</summary>
    public bool ApprovalRequested { get; set; }

    /// <summary>Gets or sets the original-source provenance attestation.</summary>
    public bool ProvenanceVerified { get; set; }

    /// <summary>Gets or sets the included-content attestation.</summary>
    public bool ContentVerified { get; set; }

    /// <summary>Gets or sets the safety attestation.</summary>
    public bool SafetyVerified { get; set; }

    /// <summary>Gets or sets review rationale and unresolved issues.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Builds a request bound to the exact loaded snapshot and saved selection.</summary>
    public DocumentApprovalRequest Request() => new()
    {
        Sequencer = review.Sequencer,
        ETag = review.ETag,
        ExpectedRevision = review.Approval?.Revision ?? 0,
        ChunkReviewRevision = review.ChunkReview?.Revision ?? 0,
        Language = Language,
        DocumentType = DocumentType,
        Edition = string.IsNullOrWhiteSpace(Edition) ? null : Edition.Trim(),
        CefrLevel = string.IsNullOrWhiteSpace(CefrLevel) ? null : CefrLevel,
        ApprovalRequested = ApprovalRequested,
        ProvenanceVerified = ProvenanceVerified,
        ContentVerified = ContentVerified,
        SafetyVerified = SafetyVerified,
        Notes = Notes,
    };

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var request = Request();
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
