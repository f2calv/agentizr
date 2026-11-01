using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

internal sealed partial class DocumentSynchronizationService
{
    private async Task SynchronizeAsync(DocumentSourceConfig source, Guid runId, CancellationToken token)
    {
        await foreach (var listed in documentBlobInventory.ListAsync(source, token))
            await ObserveAsync(source, runId, listed.BlobUri, false, token);

        // Absence-based cleanup starts only after the full inventory completed successfully.
        while (true)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(token);
            var prefix = source.ContainerUri.TrimEnd('/') + "/";
            var missing = await db.KnowledgeDocuments.AsNoTracking().Where(item =>
                    item.TenantId == source.TenantId && item.CollectionId == source.CollectionId
                    && item.BlobUri.StartsWith(prefix) && item.Status != DocumentIngestionStates.Deleted
                    && (item.LastSeenSyncId == null || item.LastSeenSyncId != runId))
                .OrderBy(item => item.Id).Select(item => item.BlobUri).Take(100).ToArrayAsync(token);
            if (missing.Length == 0)
                return;
            foreach (var uri in missing)
                await ObserveAsync(source, runId, uri, true, token);
        }
    }

    private async Task ObserveAsync(
        DocumentSourceConfig source, Guid runId, string uri, bool allowRetirement, CancellationToken token)
    {
        var id = DocumentIngestionIdentity.Digest(source.TenantId, source.CollectionId, uri);
        await using var read = await dbContextFactory.CreateDbContextAsync(token);
        var observed = await read.KnowledgeDocuments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
        var current = await documentBlobInventory.ReadAsync(uri, token);
        if (current is null && !allowRetirement)
            return;
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var state = await OwnedAsync(db, source, runId, token);
        if (state is null)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("The synchronization claim is no longer current.");
        }
        var document = await db.KnowledgeDocuments.SingleOrDefaultAsync(item => item.Id == id, token);
        if (document?.ETag != observed?.ETag || document?.Sequencer != observed?.Sequencer
            || document?.Status != observed?.Status)
        {
            state.ConflictCount++;
            if (document is not null)
                document.LastSeenSyncId = runId;
        }
        else if (current is null && document is not null)
        {
            await DocumentSourceRetirement.PurgeAsync(db, document, token);
            document.Status = DocumentIngestionStates.Deleted;
            document.UpdatedAtUtc = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
            document.LastSeenSyncId = runId;
            state.RemovedCount++;
        }
        else if (current is not null)
            await ApplyCurrentAsync(db, state, source, document, current, token);
        await db.SaveChangesAsync(token);
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(token);
    }

    private async Task ApplyCurrentAsync(
        AgentRuntimeDbContext db,
        DocumentSynchronizationEntity state,
        DocumentSourceConfig source,
        KnowledgeDocumentEntity? document,
        DocumentBlobMetadata current,
        CancellationToken token)
    {
        var added = document is null;
        var changed = document is not null && !DocumentIngestionIdentity.SameETag(document.ETag, current.ETag);
        if (document?.Status == DocumentIngestionStates.Deleted)
        {
            state.ConflictCount++;
            document.LastSeenSyncId = state.RunId;
            return;
        }
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        if (document is null)
        {
            document = new()
            {
                Id = DocumentIngestionIdentity.Digest(source.TenantId, source.CollectionId, current.BlobUri),
                TenantId = source.TenantId,
                CollectionId = source.CollectionId,
                BlobUri = current.BlobUri,
                // Zero denotes no observed storage ordering token; this is not a fabricated storage event.
                Sequencer = new string('0', 128),
                ETag = current.ETag,
                Status = DocumentIngestionStates.Queued,
                UpdatedAtUtc = now,
            };
            db.KnowledgeDocuments.Add(document);
        }
        else if (changed)
        {
            await DocumentSourceRetirement.PurgeAsync(db, document, token);
            await db.DocumentIngestionJobs.Where(job => job.DocumentId == document.Id
                    && job.TenantId == source.TenantId && job.CollectionId == source.CollectionId)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIngestionStates.Ignored), token);
            document.ETag = current.ETag;
            document.Status = DocumentIngestionStates.Queued;
            document.UpdatedAtUtc = now;
        }
        document.LastSeenSyncId = state.RunId;
        var job = changed || added ? null : await db.DocumentIngestionJobs.SingleOrDefaultAsync(item =>
            item.DocumentId == document.Id && item.TenantId == source.TenantId && item.CollectionId == source.CollectionId
            && item.Sequencer == document.Sequencer && item.Status != DocumentIngestionStates.Ignored, token);
        if (job is null)
        {
            db.DocumentIngestionJobs.Add(new()
            {
                Id = DocumentIngestionIdentity.Digest("synchronization", document.Id, current.ETag, Guid.NewGuid().ToString()),
                Fingerprint = DocumentIngestionIdentity.Digest(document.Id, document.Sequencer, current.ETag, bool.FalseString),
                DocumentId = document.Id,
                TenantId = source.TenantId,
                CollectionId = source.CollectionId,
                Sequencer = document.Sequencer,
                Status = DocumentIngestionStates.Queued,
                AcceptedAtUtc = now,
                PreparationPlanJson = await DocumentPreparationPlanService.PinAsync(db, source.TenantId, source.AgentPreparation, token),
            });
            if (added)
                state.AddedCount++;
            else
                state.RepairedCount++;
        }
        else if (job.Status == DocumentIngestionStates.Queued && job.AttemptCount == 0
            && job.PreparationPlanJson is null && source.AgentPreparation is not null
            && !await db.DocumentPreparationOperations.AnyAsync(item => item.JobId == job.Id, token))
        {
            job.PreparationPlanJson = await DocumentPreparationPlanService.PinAsync(db, source.TenantId, source.AgentPreparation, token);
            state.RepairedCount++;
        }
        else
            state.UnchangedCount++;
    }
}
