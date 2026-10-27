using Azure;

namespace CasCap.Services;

/// <summary>Downloads and stages complete PDF preparation; never embeds or activates a searchable generation.</summary>
internal sealed class PdfDocumentProcessor(
    DocumentBlobDownloadService documentBlobDownloadSvc,
    PdfDocumentPreparationService pdfDocumentPreparationSvc,
    DocumentPreparationStoreService documentPreparationStoreSvc) : IDocumentProcessor
{
    /// <inheritdoc/>
    public async Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(claim.ETag))
            return DocumentProcessingOutcome.PermanentFailure;
        PdfPreparationResult preparation;
        try
        {
            Stream source;
            try
            {
                source = await documentBlobDownloadSvc.OpenReadAsync(claim, cancellationToken);
            }
            catch (ArgumentException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DocumentProcessingOutcome.PermanentFailure;
            }
            await using (source)
                preparation = await pdfDocumentPreparationSvc.PrepareAsync(source, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DocumentProcessingOutcome.PermanentFailure;
        }
        catch (RequestFailedException exception) when (exception.Status is 408 or 429 or >= 500 and <= 599)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DocumentProcessingOutcome.RetryableFailure;
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DocumentProcessingOutcome.RetryableFailure;
        }
        if (preparation.Status != PdfPreparationStatus.Prepared)
            return DocumentProcessingOutcome.PermanentFailure;
        return await documentPreparationStoreSvc.StoreAsync(claim, preparation, cancellationToken)
            ? DocumentProcessingOutcome.Completed : DocumentProcessingOutcome.ClaimLost;
    }
}
