using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using static CasCap.Services.DocumentIngestionIdentity;

namespace CasCap.Services;

/// <summary>Serializes admission in PostgreSQL and rejects stale events independently of Redis leadership.</summary>
internal sealed class DocumentIngestionInboxService(
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    IDocumentBlobInventory? documentBlobInventory = null) : IDocumentIngestionInbox
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
        if (document?.LastSeenSyncId is not null && documentBlobInventory is null)
            throw new InvalidOperationException("Synchronized source admission requires current storage verification.");
        var current = documentBlobInventory is null ? null
            : await documentBlobInventory.ReadAsync(notification.Event.BlobUri.AbsoluteUri, cancellationToken);
        var status = notification.Event.Deleted ? DocumentIngestionStates.Deleted : DocumentIngestionStates.Queued;
        var obsolete = documentBlobInventory is not null
            && (notification.Event.Deleted ? current is not null : !SameETag(current?.ETag, notification.Event.ETag));
        if (obsolete)
            status = DocumentIngestionStates.Ignored;
        if (document is null && obsolete)
        {
            // Obsolete deliveries need no durable processing intent; queue acknowledgement remains safe.
            await transaction.CommitAsync(cancellationToken);
            return;
        }
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
        else if (obsolete || string.CompareOrdinal(notification.Event.Sequencer, document.Sequencer) <= 0
            || document.Status == DocumentIngestionStates.Deleted
            || document.LastSeenSyncId is not null && !notification.Event.Deleted
                && SameETag(document.ETag, notification.Event.ETag))
        {
            // Immutable source names are never resurrected by event replay or a later overwrite.
            status = DocumentIngestionStates.Ignored;
        }

        if (status != DocumentIngestionStates.Ignored)
        {
            if (notification.Event.Deleted || document.ETag is not null && document.ETag != notification.Event.ETag)
                await DocumentSourceRetirement.PurgeAsync(db, document, cancellationToken);
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

}
