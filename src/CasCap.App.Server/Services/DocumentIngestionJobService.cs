using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Owns transactional claims and outcomes independently of Redis leadership.</summary>
internal sealed class DocumentIngestionJobService(
    IOptions<DocumentIngestionConfig> config,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory)
{
    /// <summary>Recovers expired attempts and claims one current due revision, or returns null when gated or idle.</summary>
    public async Task<DocumentIngestionClaim?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (!config.Value.ProcessingEnabled)
            return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken);
        await RecoverAsync(db, now, cancellationToken);
        var job = await db.DocumentIngestionJobs
            .Where(job => job.AttemptCount < DocumentProcessingPolicy.MaxAttempts
                && (job.Status == DocumentIngestionStates.Queued
                    || (job.Status == DocumentIngestionStates.RetryPending && job.NextAttemptAtUtc <= now))
                && db.KnowledgeDocuments.Any(document => document.Id == job.DocumentId
                    && document.TenantId == job.TenantId && document.CollectionId == job.CollectionId
                    && document.Sequencer == job.Sequencer && document.Status != DocumentIngestionStates.Deleted))
            .OrderBy(job => job.Id).FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var document = await db.KnowledgeDocuments.SingleAsync(item => item.Id == job.DocumentId, cancellationToken);
        job.Status = DocumentIngestionStates.Processing;
        job.AttemptCount++;
        job.ClaimId = Guid.NewGuid();
        job.ClaimExpiresAtUtc = now.AddSeconds(config.Value.ProcessingTimeoutSeconds);
        job.NextAttemptAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DocumentIngestionClaim
        {
            JobId = job.Id,
            ClaimId = job.ClaimId.Value,
            DocumentId = job.DocumentId,
            TenantId = job.TenantId,
            CollectionId = job.CollectionId,
            Sequencer = job.Sequencer,
            BlobUri = document.BlobUri,
            ETag = document.ETag,
            ExpiresAtUtc = job.ClaimExpiresAtUtc.Value,
        };
    }

    /// <summary>Records an outcome only for an unexpired current claim and source revision.</summary>
    public async Task<bool> FinishAsync(
        DocumentIngestionClaim claim,
        DocumentProcessingOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        if (outcome == DocumentProcessingOutcome.ClaimLost)
            return false;
        var failureCode = outcome switch
        {
            DocumentProcessingOutcome.Completed => null,
            DocumentProcessingOutcome.RetryableFailure => DocumentProcessingPolicy.RetryableFailure,
            DocumentProcessingOutcome.PermanentFailure => DocumentProcessingPolicy.PermanentFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken);
        var job = await DocumentIngestionTransactions.CurrentClaim(db, claim, now).SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var retry = outcome == DocumentProcessingOutcome.RetryableFailure
            && job.AttemptCount < DocumentProcessingPolicy.MaxAttempts;
        job.Status = retry ? DocumentIngestionStates.RetryPending
            : outcome == DocumentProcessingOutcome.Completed ? DocumentIngestionStates.Completed : DocumentIngestionStates.Failed;
        job.LastFailureCode = failureCode;
        job.ClaimId = null;
        job.NextAttemptAtUtc = retry ? now.AddSeconds(DocumentProcessingPolicy.RetryDelaySeconds) : null;
        job.FinishedAtUtc = retry ? null : now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task RecoverAsync(AgentRuntimeDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = db.DocumentIngestionJobs.Where(job => job.Status == DocumentIngestionStates.Processing
            && job.ClaimExpiresAtUtc <= now);
        await expired.Where(job => job.AttemptCount >= DocumentProcessingPolicy.MaxAttempts)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIngestionStates.Failed)
                .SetProperty(job => job.ClaimId, (Guid?)null)
                .SetProperty(job => job.FinishedAtUtc, now)
                .SetProperty(job => job.LastFailureCode, DocumentProcessingPolicy.DeadlineExpired), cancellationToken);
        await expired.Where(job => job.AttemptCount < DocumentProcessingPolicy.MaxAttempts)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIngestionStates.RetryPending)
                .SetProperty(job => job.ClaimId, (Guid?)null)
                .SetProperty(job => job.NextAttemptAtUtc, now.AddSeconds(DocumentProcessingPolicy.RetryDelaySeconds))
                .SetProperty(job => job.LastFailureCode, DocumentProcessingPolicy.DeadlineExpired), cancellationToken);
    }
}
