using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Shares the PoC admission/job mutation lock; callers own the surrounding transaction.</summary>
internal static class DocumentIngestionTransactions
{
    /// <summary>Serializes every admission, claim and outcome mutation across PostgreSQL replicas.</summary>
    public static async Task LockAsync(AgentRuntimeDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"agent_runtime:document_ingestion:admission"}, 0))",
                cancellationToken);
    }

    /// <summary>Uses database time for PostgreSQL deadlines and the injected clock in isolated SQLite tests.</summary>
    public static Task<DateTimeOffset> GetUtcNowAsync(
        AgentRuntimeDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    ) =>
        db.Database.IsNpgsql()
            ? db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken)
            : Task.FromResult(timeProvider.GetUtcNow());

    /// <summary>Fences staged writes and outcomes against the same exact live claim and admitted source.</summary>
    public static IQueryable<DocumentIngestionJobEntity> CurrentClaim(
        AgentRuntimeDbContext db,
        DocumentIngestionClaim claim,
        DateTimeOffset now
    ) => db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId && job.ClaimId == claim.ClaimId
        && job.DocumentId == claim.DocumentId && job.Sequencer == claim.Sequencer
        && job.TenantId == claim.TenantId && job.CollectionId == claim.CollectionId
        && job.PreparationPlanJson == claim.PreparationPlanJson
        && job.Status == DocumentIngestionStates.Processing && job.ClaimExpiresAtUtc > now
        && db.KnowledgeDocuments.Any(document => document.Id == job.DocumentId
            && document.TenantId == job.TenantId && document.CollectionId == job.CollectionId
            && document.Sequencer == claim.Sequencer && document.ETag == claim.ETag
            && document.BlobUri == claim.BlobUri && document.Status != DocumentIngestionStates.Deleted));

    /// <summary>Fences explicit review storage/reads by a finished job and current source, not a released processing lease.</summary>
    public static IQueryable<DocumentIngestionJobEntity> CurrentReviewSource(AgentRuntimeDbContext db, DocumentIngestionClaim source) =>
        db.DocumentIngestionJobs.Where(job => source.PreparationPlanJson != null && source.ETag != null
            && job.Id == source.JobId && job.TenantId == source.TenantId && job.CollectionId == source.CollectionId
            && job.DocumentId == source.DocumentId && job.Sequencer == source.Sequencer
            && job.PreparationPlanJson == source.PreparationPlanJson && job.Status == DocumentIngestionStates.ReviewRequired
            && job.ClaimId == null && job.FinishedAtUtc != null
            && db.KnowledgeDocuments.Any(document => document.Id == job.DocumentId
                && document.TenantId == job.TenantId && document.CollectionId == job.CollectionId
                && document.Sequencer == source.Sequencer && document.ETag == source.ETag
                && document.BlobUri == source.BlobUri && document.Status != DocumentIngestionStates.Deleted));
}
