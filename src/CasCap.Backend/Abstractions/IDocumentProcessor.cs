namespace CasCap.Abstractions;

/// <summary>Processes one claimed source revision; implementations must honor cancellation and stage outputs safely.</summary>
internal interface IDocumentProcessor
{
    /// <summary>Returns only expected outcomes; infrastructure and programming faults escape to host supervision.</summary>
    public Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken);
}
