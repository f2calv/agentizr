#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Models;

/// <summary>One transactionally consistent source, snapshot, selection and readiness observation.</summary>
internal sealed record DocumentIndexingReview(
    DocumentIngestionClaim Source,
    string GenerationId,
    DocumentMarkdownReviewGeneration Snapshot,
    DocumentChunkReviewResponse? Selection,
    DocumentApprovalResponse? Approval);
#endif
