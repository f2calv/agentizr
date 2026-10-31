#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Services;

public sealed partial class DocumentReviewAdministrationService
{
    /// <summary>Records a version-bound readiness assessment, without running providers or authorizing indexing.</summary>
    public async Task<DocumentReviewGenerationOutcome> RecordApprovalAsync(
        string collectionId, string documentId, string jobId, string generationId, DocumentApprovalRequest request, CancellationToken token)
    {
        if (_dbContextFactory is null || _markdownStore is null)
            return new() { IsSupported = false };
        var source = await SourceAsync(collectionId, documentId, jobId, token);
        if (source is null)
            return new();
        if (request.Sequencer != source.Sequencer || request.ETag != source.ETag)
            return new() { IsConflict = true };
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true))
            return new() { ValidationError = "Document classification, review notes or observed revisions are invalid." };
        var snapshot = await _markdownStore.ReadAsync(source, generationId, token);
        if (snapshot is null)
            return new();
        var approval = await _markdownStore.StoreApprovalAsync(source, generationId, request, _actorContext.ActorId, token);
        return approval is null ? new() { IsConflict = true } : new()
        {
            Generation = Response(source, snapshot, generationId) with
            {
                ChunkReview = await _markdownStore.ReadChunkReviewAsync(source, generationId, token),
                Approval = approval,
            },
        };
    }
}
#endif
