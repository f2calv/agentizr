namespace CasCap.Services;

/// <summary>Awaits one processor attempt inside the elected lifetime; never detaches uncooperative work.</summary>
internal sealed partial class DocumentIngestionProcessingService(
    ILogger<DocumentIngestionProcessingService> logger,
    IOptions<DocumentIngestionConfig> config,
    TimeProvider timeProvider,
    DocumentIngestionJobService documentIngestionJobSvc,
    IEnumerable<IDocumentProcessor> documentProcessors)
{
    /// <summary>Processes at most one job; disabled processing never resolves a processor or claims work.</summary>
    public async Task<bool> ProcessOneAsync(CancellationToken ownershipToken)
    {
        if (!config.Value.ProcessingEnabled)
            return false;
        var processor = documentProcessors.SingleOrDefault()
            ?? throw new InvalidOperationException("Document processing requires a real registered document processor.");
        var claim = await documentIngestionJobSvc.ClaimAsync(ownershipToken);
        if (claim is null)
            return false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(config.Value.ProcessingTimeoutSeconds), timeProvider);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ownershipToken, deadline.Token);
        try
        {
            DocumentProcessingOutcome outcome;
            try
            {
                attempt.Token.ThrowIfCancellationRequested();
                outcome = await processor.ProcessAsync(claim, attempt.Token);
                ownershipToken.ThrowIfCancellationRequested();
                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ownershipToken.IsCancellationRequested)
            {
                // Leave the expired claim for durable recovery; do not extend its deadline with a late outcome.
                LogDeadline(logger, nameof(DocumentIngestionProcessingService));
                return true;
            }
            ownershipToken.ThrowIfCancellationRequested();
            var applied = await documentIngestionJobSvc.FinishAsync(claim, outcome, ownershipToken);
            LogOutcome(logger, nameof(DocumentIngestionProcessingService), outcome, applied);
            return true;
        }
        finally
        {
            await attempt.CancelAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} attempt deadline expired; durable recovery will retry or fail the job.")]
    private static partial void LogDeadline(ILogger logger, string className);

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} processor outcome={Outcome} applied={Applied}.")]
    private static partial void LogOutcome(ILogger logger, string className, DocumentProcessingOutcome outcome, bool applied);
}
