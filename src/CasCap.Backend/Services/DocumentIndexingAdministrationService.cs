#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Services;

/// <summary>Separately authorizes durable indexing through the existing tenant-scoped administration source boundary.</summary>
public sealed class DocumentIndexingAdministrationService
{
    private readonly DocumentReviewAdministrationService _documentReviewAdministrationSvc;
    private readonly IActorContext _actorContext;
    private readonly DocumentIndexingStore? _store;

    internal DocumentIndexingAdministrationService(
        DocumentReviewAdministrationService documentReviewAdministrationSvc, IActorContext actorContext, DocumentIndexingStore? store)
    {
        _documentReviewAdministrationSvc = documentReviewAdministrationSvc;
        _actorContext = actorContext;
        _store = store;
    }

    /// <summary>Commits explicit bounded authorization, never from a readiness save or preflight GET.</summary>
    public async Task<DocumentIndexingJobOutcome> AcceptAsync(
        string collectionId, string documentId, string jobId, string generationId, DocumentIndexingRequest request, CancellationToken token)
    {
        if (_store is null)
            return new() { IsSupported = false };
        var source = await _documentReviewAdministrationSvc.SourceAsync(collectionId, documentId, jobId, token);
        return source is null ? new() : await _store.AcceptAsync(source, generationId, request, _actorContext.ActorId, token);
    }

    /// <summary>Reads payload-free job progress with current effective eligibility, not just a persisted publication flag.</summary>
    public async Task<DocumentIndexingJobOutcome> ReadAsync(
        string collectionId, string documentId, string jobId, string generationId, string indexingJobId, CancellationToken token)
    {
        if (_store is null)
            return new() { IsSupported = false };
        var source = await _documentReviewAdministrationSvc.SourceAsync(collectionId, documentId, jobId, token);
        return source is null ? new() : await _store.ReadAsync(source, generationId, indexingJobId, token);
    }
}
#endif
