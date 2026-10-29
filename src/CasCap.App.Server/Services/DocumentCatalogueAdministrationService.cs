#if DEBUG
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Reads bounded tenant-qualified catalogue metadata, never source contents, plans or preparation receipts.</summary>
public sealed class DocumentCatalogueAdministrationService
{
    private readonly ITenantContext _tenantContext;
    private readonly IDbContextFactory<AgentRuntimeDbContext>? _database;

    /// <summary>Creates a catalogue reader using the authenticated app's tenant context.</summary>
    internal DocumentCatalogueAdministrationService(
        ITenantContext tenantContext,
        IDbContextFactory<AgentRuntimeDbContext>? database)
    {
        _tenantContext = tenantContext;
        _database = database;
    }

    /// <summary>Lists source/current-job metadata, or returns null when durable storage is not configured.</summary>
    public async Task<DocumentCataloguePageResponse?> ReadAsync(
        string? collection, int page, int pageSize, CancellationToken token)
    {
        if (_database is null)
            return null;
        await using var db = await _database.CreateDbContextAsync(token);
        var tenant = _tenantContext.TenantId;
        var query =
            from document in db.KnowledgeDocuments.AsNoTracking()
            where document.TenantId == tenant && (collection == null || document.CollectionId == collection)
            join job in db.DocumentIngestionJobs.AsNoTracking()
                .Where(item => item.TenantId == tenant && item.Status != DocumentIngestionStates.Ignored)
                on new { DocumentId = document.Id, document.CollectionId, document.Sequencer }
                equals new { job.DocumentId, job.CollectionId, job.Sequencer } into jobs
            from job in jobs.DefaultIfEmpty()
            select new
            {
                document.Id,
                document.CollectionId,
                document.BlobUri,
                SourceState = document.Status,
                JobId = job == null ? null : job.Id,
                JobState = job == null ? null : job.Status,
                CanReview = job != null && job.Status == DocumentIngestionStates.ReviewRequired
                    && job.ClaimId == null && job.FinishedAtUtc != null && job.PreparationPlanJson != null
                    && document.Status != DocumentIngestionStates.Deleted && document.ETag != null,
                StoredGenerationId = db.DocumentMarkdownGenerations
                    .Where(generation => generation.TenantId == tenant && generation.CollectionId == document.CollectionId
                        && generation.DocumentId == document.Id && job != null && generation.JobId == job.Id
                        && generation.Sequencer == document.Sequencer && generation.ETag == document.ETag
                        && document.Status != DocumentIngestionStates.Deleted)
                    .OrderBy(generation => generation.Id).Select(generation => generation.Id).FirstOrDefault(),
            };
        var count = await query.CountAsync(token);
        var rows = await query.OrderBy(item => item.CollectionId).ThenBy(item => item.Id).ThenBy(item => item.JobId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return new()
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = count,
            Items = rows.Select(row => new DocumentCatalogueItemResponse
            {
                DocumentId = row.Id,
                CollectionId = row.CollectionId,
                FileName = Uri.UnescapeDataString(new Uri(row.BlobUri).Segments[^1]),
                SourceState = row.SourceState,
                JobId = row.JobId,
                JobState = row.JobState,
                CanReview = row.CanReview,
                StoredGenerationId = row.StoredGenerationId,
            }).ToArray(),
        };
    }
}
#endif
