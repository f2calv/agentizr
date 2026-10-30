using CasCap.Client.Models;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

internal sealed partial class DocumentPreparationMarkdownStore
{
    /// <summary>Builds a consistent read-only input manifest under the existing source/selection mutation fence.</summary>
    public async Task<DocumentIndexingPreflight?> PreflightIndexingAsync(
        DocumentIngestionClaim source, string generationId, DocumentIndexingConfig settings, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(token))
            return null;
        var generation = await db.DocumentMarkdownGenerations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == generationId && item.TenantId == source.TenantId && item.CollectionId == source.CollectionId
            && item.DocumentId == source.DocumentId && item.JobId == source.JobId
            && item.Sequencer == source.Sequencer && item.ETag == source.ETag, token);
        if (generation is null)
            return null;
        var snapshot = ValidateStored(source, generation);
        var selectionRecord = await db.DocumentChunkReviews.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        var approvalRecord = await db.DocumentApprovals.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        DocumentChunkReviewResponse? selection = selectionRecord is null ? null : ChunkReviewResponse(selectionRecord);
        var approval = approvalRecord is null ? null : ApprovalResponse(
            approvalRecord, selectionRecord, snapshot.Projection.Chunks.Select(chunk => chunk.Ordinal).ToHashSet());
        var result = DocumentIndexingPlanBuilder.Build(settings, new(source, generationId, snapshot, selection, approval));
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(token))
            return null;
        await transaction.CommitAsync(token);
        return result;
    }
}
