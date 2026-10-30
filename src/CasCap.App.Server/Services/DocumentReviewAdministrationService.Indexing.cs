#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Services;

public sealed partial class DocumentReviewAdministrationService
{
    /// <summary>Checks current review inputs against trusted collection policy without persisting or invoking providers.</summary>
    public async Task<DocumentIndexingPreflightOutcome> PreflightIndexingAsync(
        string collectionId, string documentId, string jobId, string generationId, CancellationToken token)
    {
        if (_dbContextFactory is null || _markdownStore is null)
            return new() { IsSupported = false };
        var source = await SourceAsync(collectionId, documentId, jobId, token);
        if (source is null)
            return new();
        return new()
        {
            Preflight = (await _markdownStore.PreflightIndexingAsync(source, generationId, _indexingConfig.Value, token))?.Response,
        };
    }
}
#endif
