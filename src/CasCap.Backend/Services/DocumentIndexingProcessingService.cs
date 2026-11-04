#if DEBUG
namespace CasCap.Services;

/// <summary>Runs one claimed batch; durable submission intent survives ownership loss, timeout and process failure.</summary>
internal sealed class DocumentIndexingProcessingService(
    ILogger<DocumentIndexingProcessingService> logger,
    IOptions<DocumentIndexingConfig> config,
    DocumentIndexingStore documentIndexingStore,
    IDocumentEmbeddingProvider documentEmbeddingProvider)
{
    public async Task<bool> ProcessOneAsync(CancellationToken token)
    {
        var claim = await documentIndexingStore.ClaimAsync(token);
        if (claim is null)
            return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.Value.ProcessingTimeoutSeconds));
        var submitted = false;
        try
        {
            await documentEmbeddingProvider.VerifyAsync(claim.Manifest, deadline.Token);
            var batch = await documentIndexingStore.ReserveAsync(claim, deadline.Token);
            if (batch is null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("{ClassName} submission was not reserved because pacing, ownership, eligibility or budget changed",
                        nameof(DocumentIndexingProcessingService));
                return true;
            }
            submitted = true;
            var result = await documentEmbeddingProvider.GenerateAsync(batch, deadline.Token);
            if (!await documentIndexingStore.ReceiveAsync(batch, result, deadline.Token) && logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("{ClassName} embedding receipt retained without publication because its authorization, ownership or budget changed",
                    nameof(DocumentIndexingProcessingService));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException
            or ValidationException or OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("{ClassName} stopped with outcome {Outcome}; HTTP status {HttpStatusCode}; submitted intent {Submitted}",
                    nameof(DocumentIndexingProcessingService), ex.GetType().Name,
                    ex is HttpRequestException { StatusCode: { } status } ? (int?)status : null, submitted);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await documentIndexingStore.FailAsync(claim, submitted,
                ex is OperationCanceledException && (token.IsCancellationRequested || deadline.IsCancellationRequested), cleanup.Token);
            token.ThrowIfCancellationRequested();
        }
        return true;
    }
}
#endif
