using CasCap.Client.Models;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

internal sealed partial class DocumentPreparationMarkdownStore
{
    /// <summary>Reads an assessment only for the current source, deriving currency from the latest saved selection.</summary>
    public async Task<DocumentApprovalResponse?> ReadApprovalAsync(
        DocumentIngestionClaim source, string generationId, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var current = DocumentIngestionTransactions.CurrentReviewSource(db, source);
        var record = await db.DocumentApprovals.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId && current.Any())
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        if (record is null)
            return null;
        var selection = await db.DocumentChunkReviews.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        var generation = await db.DocumentMarkdownGenerations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TenantId == source.TenantId && item.Id == generationId, token);
        if (generation is null)
            return null;
        var snapshot = ValidateStored(source, generation);
        return await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(token)
            ? ApprovalResponse(record, selection, snapshot.Projection.Chunks.Select(chunk => chunk.Ordinal).ToHashSet()) : null;
    }

    /// <summary>Appends an authenticated assessment under the same source/selection mutation lock.</summary>
    public async Task<DocumentApprovalResponse?> StoreApprovalAsync(
        DocumentIngestionClaim source, string generationId, DocumentApprovalRequest request, string actorId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var normalized = request with { Notes = request.Notes.Trim(), Edition = request.Edition?.Trim() };
        var payload = JsonSerializer.Serialize(normalized, JsonSerializerOptions.Web);
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
        var candidates = snapshot.Projection.Chunks.Select(chunk => chunk.Ordinal).ToHashSet();
        var selection = await db.DocumentChunkReviews.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        if (selection is null || selection.Revision != request.ChunkReviewRevision)
            return null;
        var latest = await db.DocumentApprovals.AsNoTracking()
            .Where(item => item.TenantId == source.TenantId && item.GenerationId == generationId)
            .OrderByDescending(item => item.Revision).FirstOrDefaultAsync(token);
        if ((latest?.Revision ?? 0) != request.ExpectedRevision)
            return latest is not null && latest.Revision == request.ExpectedRevision + 1
                && latest.ActorId == actorId
                && JsonSerializer.Serialize(ReadAssessment(latest), JsonSerializerOptions.Web) == payload
                    ? ApprovalResponse(latest, selection, candidates) : null;
        var record = new DocumentApprovalEntity
        {
            TenantId = source.TenantId,
            GenerationId = generationId,
            Revision = checked(request.ExpectedRevision + 1),
            ChunkReviewRevision = request.ChunkReviewRevision,
            ActorId = actorId,
            AssessmentJson = payload,
            RecordedAtUtc = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token),
        };
        var response = ApprovalResponse(record, selection, candidates);
        db.DocumentApprovals.Add(record);
        await db.SaveChangesAsync(token);
        if (!await DocumentIngestionTransactions.CurrentReviewSource(db, source).AnyAsync(token))
            return null;
        await transaction.CommitAsync(token);
        return response;
    }

    private static DocumentApprovalRequest ReadAssessment(DocumentApprovalEntity record) =>
        JsonSerializer.Deserialize<DocumentApprovalRequest>(record.AssessmentJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The saved document assessment is empty.");

    private static DocumentApprovalResponse ApprovalResponse(
        DocumentApprovalEntity record, DocumentChunkReviewEntity? selection, HashSet<int> candidates)
    {
        var assessment = ReadAssessment(record);
        Validator.ValidateObject(assessment, new ValidationContext(assessment), validateAllProperties: true);
        if (record.ChunkReviewRevision != assessment.ChunkReviewRevision)
            throw new InvalidDataException("The saved assessment selection binding is inconsistent.");
        var current = selection?.Revision == record.ChunkReviewRevision;
        var exclusions = selection is null ? [] : ChunkReviewResponse(selection).Exclusions;
        if (exclusions.Any(item => item is null || !candidates.Contains(item.Ordinal))
            || exclusions.Select(item => item.Ordinal).Distinct().Count() != exclusions.Count)
            throw new InvalidDataException("The saved assessment selection does not match the snapshot.");
        var included = candidates.Count - exclusions.Count;
        var blockers = assessment.Blockers(included).ToList();
        if (!current)
            blockers.Add("The saved chunk selection changed; reassessment is required.");
        return new()
        {
            Revision = record.Revision,
            RecordedAtUtc = record.RecordedAtUtc,
            Assessment = assessment,
            IsCurrent = current,
            Blockers = blockers,
        };
    }
}
