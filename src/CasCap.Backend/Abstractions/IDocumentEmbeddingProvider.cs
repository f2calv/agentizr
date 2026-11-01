#if DEBUG
namespace CasCap.Abstractions;

/// <summary>Separates safe metadata verification from one explicitly reserved, non-retried paid submission.</summary>
internal interface IDocumentEmbeddingProvider
{
    public Task VerifyAsync(DocumentIndexingManifest manifest, CancellationToken token);

    public Task<DocumentEmbeddingResult> GenerateAsync(DocumentEmbeddingBatch batch, CancellationToken token);
}
#endif
