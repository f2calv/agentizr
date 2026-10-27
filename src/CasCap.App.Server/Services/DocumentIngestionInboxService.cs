using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Serializes admission in PostgreSQL and rejects stale events independently of Redis leadership.</summary>
internal sealed class DocumentIngestionInboxService(
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory) : IDocumentIngestionInbox
{
    /// <inheritdoc/>
    public async Task AcceptAsync(DocumentIngestionNotification notification, CancellationToken cancellationToken)
    {
        var receiptId = Digest(notification.Event.Topic, notification.Event.Id);
        var documentId = Digest(notification.TenantId, notification.CollectionId, notification.Event.BlobUri.AbsoluteUri);
        var fingerprint = Digest(documentId, notification.Event.Sequencer, notification.Event.ETag,
            notification.Event.Deleted.ToString());
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        var receipt = await db.DocumentIngestionJobs.SingleOrDefaultAsync(job => job.Id == receiptId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.Fingerprint != fingerprint)
                throw new JsonException("The notification identity was reused with different document data.");
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var document = await db.KnowledgeDocuments.SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        var status = notification.Event.Deleted ? DocumentIngestionStates.Deleted : DocumentIngestionStates.Queued;
        if (document is null)
        {
            document = new KnowledgeDocumentEntity
            {
                Id = documentId,
                TenantId = notification.TenantId,
                CollectionId = notification.CollectionId,
                BlobUri = notification.Event.BlobUri.AbsoluteUri,
                Sequencer = notification.Event.Sequencer,
                Status = status,
            };
            db.KnowledgeDocuments.Add(document);
        }
        else if (string.CompareOrdinal(notification.Event.Sequencer, document.Sequencer) <= 0
            || document.Status == DocumentIngestionStates.Deleted)
        {
            // Immutable source names are never resurrected by event replay or a later overwrite.
            status = DocumentIngestionStates.Ignored;
        }

        if (status != DocumentIngestionStates.Ignored)
        {
            document.Sequencer = notification.Event.Sequencer;
            document.ETag = notification.Event.ETag;
            document.Status = status;
            document.UpdatedAtUtc = timeProvider.GetUtcNow();
            await db.DocumentIngestionJobs
                .Where(job => job.DocumentId == documentId
                    && (job.Status == DocumentIngestionStates.Queued
                        || job.Status == DocumentIngestionStates.Processing
                        || job.Status == DocumentIngestionStates.RetryPending))
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIngestionStates.Ignored)
                    .SetProperty(job => job.ClaimId, (Guid?)null)
                    .SetProperty(job => job.NextAttemptAtUtc, (DateTimeOffset?)null),
                    cancellationToken);
        }
        db.DocumentIngestionJobs.Add(new DocumentIngestionJobEntity
        {
            Id = receiptId,
            Fingerprint = fingerprint,
            DocumentId = documentId,
            TenantId = notification.TenantId,
            CollectionId = notification.CollectionId,
            Sequencer = notification.Event.Sequencer,
            Status = status,
            AcceptedAtUtc = timeProvider.GetUtcNow(),
            PreparationPlanJson = status == DocumentIngestionStates.Queued
                ? await DocumentPreparationPlanService.PinAsync(db, notification, cancellationToken)
                : null,
        });
        cancellationToken.ThrowIfCancellationRequested();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Digest(params string?[] values) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
}
