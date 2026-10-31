namespace CasCap.Services;

/// <summary>Routes exclusively by the persisted admission plan, never by mutable current preparation configuration.</summary>
internal sealed class DocumentPreparationProcessor(
    PdfDocumentProcessor pdfDocumentProcessor,
    HybridDocumentProcessor hybridDocumentProcessor) : IDocumentProcessor
{
    /// <inheritdoc/>
    public Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken) =>
        claim.PreparationPlanJson is null
            ? pdfDocumentProcessor.ProcessAsync(claim, cancellationToken)
            : hybridDocumentProcessor.ProcessAsync(claim, cancellationToken);
}
