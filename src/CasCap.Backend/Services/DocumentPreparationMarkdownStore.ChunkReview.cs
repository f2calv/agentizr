using CasCap.Client.Models;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

internal sealed partial class DocumentPreparationMarkdownStore
{
    /// <summary>Reads the latest review only while the exact owning source and snapshot remain available.</summary>
    public async Task<DocumentChunkReviewResponse?> ReadChunkReviewAsync(
        DocumentIngestionClaim source, string generationId, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var current = DocumentIngestionTransactions.CurrentReviewSource(db, source);
        var item = await db.DocumentChunkReviews.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId && current.Any())
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        return item is null ? null : ChunkReviewResponse(item);
    }

    /// <summary>Appends a review under the admission lock; stale observations conflict and exact current retries reuse the receipt.</summary>
    public async Task<DocumentChunkReviewResponse?> StoreChunkReviewAsync(
        DocumentIngestionClaim source, string generationId, DocumentChunkReviewRequest request, string actorId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var exclusions = request.Exclusions.OrderBy(item => item.Ordinal)
            .Select(item => item with { Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim() }).ToArray();
        var payload = JsonSerializer.Serialize(exclusions, JsonSerializerOptions.Web);
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
        if (exclusions.Any(item => !snapshot.Projection.Chunks.Any(chunk => chunk.Ordinal == item.Ordinal)))
            throw new InvalidDataException("A selection review refers to an absent snapshot candidate.");
        var latest = await db.DocumentChunkReviews.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        if ((latest?.Revision ?? 0) != request.ExpectedRevision)
            return latest is not null && latest.Revision == request.ExpectedRevision + 1
                && latest.ActorId == actorId
                && JsonSerializer.Serialize(ChunkReviewResponse(latest).Exclusions, JsonSerializerOptions.Web) == payload
                    ? ChunkReviewResponse(latest) : null;
        var record = new DocumentChunkReviewEntity
        {
            TenantId = source.TenantId,
            GenerationId = generationId,
            Revision = checked(request.ExpectedRevision + 1),
            ActorId = actorId,
            ExclusionsJson = payload,
            RecordedAtUtc = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token),
        };
        db.DocumentChunkReviews.Add(record);
        await db.SaveChangesAsync(token);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(token))
            return null;
        await transaction.CommitAsync(token);
        return ChunkReviewResponse(record);
    }

    private static DocumentChunkReviewResponse ChunkReviewResponse(DocumentChunkReviewEntity item) => new()
    {
        Revision = item.Revision,
        RecordedAtUtc = item.RecordedAtUtc,
        Exclusions = JsonSerializer.Deserialize<DocumentChunkExclusionRequest[]>(item.ExclusionsJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The saved chunk review is empty."),
    };
}
