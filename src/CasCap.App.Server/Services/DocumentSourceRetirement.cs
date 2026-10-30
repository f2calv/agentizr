using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Removes source-derived content under the ingestion transaction lock while retaining source/job/usage audit.</summary>
internal static class DocumentSourceRetirement
{
    /// <summary>Invalidates active writers and removes every currently implemented stored chunk projection.</summary>
    public static async Task PurgeAsync(
        AgentRuntimeDbContext db, KnowledgeDocumentEntity document, CancellationToken token)
    {
        var indexing = db.DocumentIndexingJobs.Where(job => job.DocumentId == document.Id
            && job.TenantId == document.TenantId && job.CollectionId == document.CollectionId);
        await db.ActiveDocumentIndexes.Where(item => item.TenantId == document.TenantId
            && item.CollectionId == document.CollectionId && item.DocumentId == document.Id).ExecuteDeleteAsync(token);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM agent_runtime.document_index_vectors WHERE tenant_id = {document.TenantId}
                AND job_id IN (SELECT id FROM agent_runtime.document_indexing_jobs
                    WHERE tenant_id = {document.TenantId} AND collection_id = {document.CollectionId} AND document_id = {document.Id})
                """, token);
        await indexing.ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIndexingStates.Revoked)
            .SetProperty(job => job.ClaimId, (Guid?)null).SetProperty(job => job.ManifestJson, (string?)null), token);
        var jobs = db.DocumentIngestionJobs.Where(job => job.DocumentId == document.Id
            && job.TenantId == document.TenantId && job.CollectionId == document.CollectionId);
        await jobs.Where(job => job.Status == DocumentIngestionStates.Queued
                || job.Status == DocumentIngestionStates.Processing || job.Status == DocumentIngestionStates.RetryPending)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, DocumentIngestionStates.Ignored)
                .SetProperty(job => job.ClaimId, (Guid?)null)
                .SetProperty(job => job.NextAttemptAtUtc, (DateTimeOffset?)null), token);
        var generations = db.DocumentPreparationGenerations.Where(item => item.DocumentId == document.Id
            && item.TenantId == document.TenantId && item.CollectionId == document.CollectionId);
        await db.DocumentTextChunks.Where(chunk => chunk.TenantId == document.TenantId
                && chunk.CollectionId == document.CollectionId && generations.Any(item => item.Id == chunk.GenerationId))
            .ExecuteDeleteAsync(token);
        await generations.ExecuteDeleteAsync(token);
        await db.DocumentMarkdownGenerations.Where(item => item.DocumentId == document.Id
                && item.TenantId == document.TenantId && item.CollectionId == document.CollectionId)
            .ExecuteDeleteAsync(token);
        await db.DocumentPreparationOperations.Where(item => item.DocumentId == document.Id
                && item.TenantId == document.TenantId && item.CollectionId == document.CollectionId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ResultJson, (string?)null), token);
    }
}
