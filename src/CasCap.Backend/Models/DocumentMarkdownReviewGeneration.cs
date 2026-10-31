using CasCap.Constants;

namespace CasCap.Models;

/// <summary>Retains normalized candidates, separate notes and source-bound chunks without semantic approval.</summary>
internal sealed record DocumentMarkdownReviewGeneration(
    int Version,
    string SourceSha256,
    string PlanSha256,
    IReadOnlyList<DocumentMarkdownReceiptReference> Receipts,
    IReadOnlyList<DocumentPreparationCandidate> Candidates,
    DocumentMarkdownChunkingResult Projection)
{
    /// <summary>Gets the fixed non-searchable state; stored candidates cannot represent approval.</summary>
    public string State => DocumentIngestionStates.ReviewRequired;
}
