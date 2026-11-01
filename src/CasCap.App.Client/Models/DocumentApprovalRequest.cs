using CasCap.Client.Constants;

namespace CasCap.Client.Models;

/// <summary>Records a version-bound readiness assessment, not an instruction to embed or publish.</summary>
public sealed record DocumentApprovalRequest : IValidatableObject
{
    /// <summary>Gets the observed source ordering token.</summary>
    /// <example>0000000000000001</example>
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Sequencer { get; init; }

    /// <summary>Gets the observed source revision.</summary>
    /// <example>"example-etag"</example>
    [Required, StringLength(1_024, MinimumLength = 1)]
    public required string ETag { get; init; }

    /// <summary>Gets the latest saved selection revision being assessed.</summary>
    /// <example>1</example>
    public required long ChunkReviewRevision { get; init; }

    /// <summary>Gets the observed assessment revision, or zero before the first assessment.</summary>
    /// <example>0</example>
    public required long ExpectedRevision { get; init; }

    /// <summary>Gets the reviewed two-letter lowercase language code.</summary>
    /// <example>de</example>
    [Required, RegularExpression("^[a-z]{2}$")]
    public required string Language { get; init; }

    /// <summary>Gets the reviewed supported document type, not a filename inference.</summary>
    /// <example>Vocabulary</example>
    [Required, MaxLength(40)]
    public required string DocumentType { get; init; }

    /// <summary>Gets an optional edition identifier.</summary>
    /// <example>Example edition</example>
    [MaxLength(200)]
    public string? Edition { get; init; }

    // TODO: Enforce trusted-collection classification and cumulative CEFR limits when retrieval is implemented;
    // this reviewer attestation cannot substitute for server-owned retrieval policy.
    /// <summary>Gets the highest reviewed content level for language-learning sources.</summary>
    /// <example>A1</example>
    [MaxLength(2)]
    public string? CefrLevel { get; init; }

    /// <summary>Gets whether the reviewer requests readiness approval rather than withholding it.</summary>
    /// <example>true</example>
    public required bool ApprovalRequested { get; init; }

    /// <summary>Gets whether source ownership/version and page provenance were checked against the original.</summary>
    /// <example>true</example>
    public required bool ProvenanceVerified { get; init; }

    /// <summary>Gets whether included content and meaning-changing uncertainties were checked and resolved.</summary>
    /// <example>true</example>
    public required bool ContentVerified { get; init; }

    /// <summary>Gets whether safety-critical material was checked and unresolved issues are absent.</summary>
    /// <example>false</example>
    public required bool SafetyVerified { get; init; }

    /// <summary>Gets the review rationale, including unresolved issues; never embedding input.</summary>
    /// <example>Checked included passages against the original; no unresolved issues.</example>
    [Required, StringLength(2_000, MinimumLength = 1)]
    public required string Notes { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpectedRevision is < 0 or long.MaxValue || ChunkReviewRevision is <= 0 or long.MaxValue)
            yield return new("Assessment and saved chunk-review revisions are invalid.",
                [nameof(ExpectedRevision), nameof(ChunkReviewRevision)]);
        if (!DocumentApprovalValues.DocumentTypes.Contains(DocumentType, StringComparer.Ordinal))
            yield return new("Select a supported reviewed document type.", [nameof(DocumentType)]);
        if (CefrLevel is not null && !DocumentApprovalValues.CefrLevels.Contains(CefrLevel, StringComparer.Ordinal))
            yield return new("Select a supported CEFR level or leave it unset.", [nameof(CefrLevel)]);
    }

    /// <summary>Returns explicit unresolved readiness gates; callers cannot supply an approved result.</summary>
    public IReadOnlyList<string> Blockers(int includedCount)
    {
        var blockers = new List<string>();
        if (!ApprovalRequested)
            blockers.Add("Readiness approval is withheld by the reviewer.");
        if (!ProvenanceVerified)
            blockers.Add("Original-source and page provenance review is unresolved.");
        if (!ContentVerified)
            blockers.Add("Included content or meaning-changing uncertainties remain unresolved.");
        if (!SafetyVerified)
            blockers.Add("Safety review is unresolved; exclusion alone cannot resolve an incorrect warning.");
        if (includedCount <= 0)
            blockers.Add("At least one included candidate is required.");
        if (DocumentType is DocumentApprovalValues.Vocabulary or DocumentApprovalValues.ExamPaper && CefrLevel is null)
            blockers.Add("A reviewed highest-content CEFR level is required.");
        if (DocumentType == DocumentApprovalValues.DeviceManual)
            blockers.Add("Manual figure/caption/source associations are not implemented and verified; visual approval remains blocked.");
        return blockers;
    }
}
