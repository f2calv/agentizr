#if DEBUG
using CasCap.Client.Models;
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Owns durable indexing acceptance, source/policy fencing and database claims, independently of process leadership.</summary>
internal sealed partial class DocumentIndexingStore(
    IOptions<DocumentIndexingConfig> config,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    DocumentPreparationMarkdownStore markdownStore)
{
    public async Task<DocumentIndexingJobOutcome> AcceptAsync(
        DocumentIngestionClaim source, string generationId, DocumentIndexingRequest request, string actorId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        if (!request.Confirmed)
            return new() { ValidationError = "Explicit confirmation of embedding submissions and publication is required." };
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (!db.Database.IsNpgsql())
            return new() { IsSupported = false };
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var review = await markdownStore.ReadIndexingReviewAsync(db, source, generationId, token);
        if (review is null)
            return new();
        var preflight = DocumentIndexingPlanBuilder.Build(config.Value, review);
        if (preflight.Manifest is not { } manifest || preflight.Response.ManifestSha256 != request.ManifestSha256)
            return new() { IsConflict = true };
        if (!manifest.Profile.HasExecutionBinding)
            return new() { ValidationError = "Exactly one explicit embedding provider binding is required." };
        var batches = (manifest.Inputs.Count + manifest.Profile.MaximumBatchInputs - 1) / manifest.Profile.MaximumBatchInputs;
        if (request.MaximumRequests < batches || request.MaximumInputTokens < preflight.Response.TotalInputTokens
            || request.MaximumInputTokens > manifest.Profile.MaximumTotalTokens)
            return new() { ValidationError = "The confirmed request and input-token ceilings do not cover the exact bounded generation." };
        var policyHash = PolicyHash(manifest);
        if (await HasNewerPolicyAsync(db, manifest, policyHash, token))
            return new() { IsConflict = true };
        var id = DocumentPreparationIdentity.Digest("document-index-v1", source.TenantId, generationId, request.ManifestSha256);
        var existing = await db.DocumentIndexingJobs.SingleOrDefaultAsync(item => item.Id == id && item.TenantId == source.TenantId, token);
        if (existing is not null)
        {
            if (existing.ActorId != actorId || existing.MaximumRequests != request.MaximumRequests
                || existing.MaximumInputTokens != request.MaximumInputTokens || existing.ExpectedActiveJobId != request.ExpectedActiveJobId)
                return new() { IsConflict = true };
            return new() { Job = await ResponseAsync(db, existing, token) };
        }
        var active = await ActiveAsync(db, manifest, token);
        if (active?.JobId != request.ExpectedActiveJobId)
            return new() { IsConflict = true };
        var job = new DocumentIndexingJobEntity
        {
            Id = id,
            TenantId = source.TenantId,
            CollectionId = source.CollectionId,
            DocumentId = source.DocumentId,
            PreparationJobId = source.JobId,
            GenerationId = generationId,
            ManifestSha256 = request.ManifestSha256,
            ManifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web),
            PolicySha256 = policyHash,
            PolicyRevision = manifest.Policy.Revision,
            ActorId = actorId,
            MaximumRequests = request.MaximumRequests,
            MaximumInputTokens = request.MaximumInputTokens,
            ExpectedActiveJobId = request.ExpectedActiveJobId,
            Status = DocumentIndexingStates.Queued,
            AcceptedAtUtc = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token),
        };
        db.DocumentIndexingJobs.Add(job);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new() { Created = true, Job = Response(job, current: true, active: false) };
    }

    public async Task<DocumentIndexingJobOutcome> ReadAsync(
        DocumentIngestionClaim source, string generationId, string id, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (!db.Database.IsNpgsql())
            return new() { IsSupported = false };
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var job = await db.DocumentIndexingJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id
            && item.TenantId == source.TenantId && item.CollectionId == source.CollectionId
            && item.DocumentId == source.DocumentId && item.PreparationJobId == source.JobId && item.GenerationId == generationId, token);
        return new() { Job = job is null ? null : await ResponseAsync(db, job, token) };
    }

    public async Task<DocumentIndexingClaim?> ClaimAsync(CancellationToken token)
    {
        if (!config.Value.Enabled || !config.Value.ProcessingEnabled)
            return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (!db.Database.IsNpgsql())
            throw new InvalidOperationException("Indexing execution requires native PostgreSQL vector storage.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        var expired = db.DocumentIndexingJobs.Where(item => item.Status == DocumentIndexingStates.Processing && item.ClaimExpiresAtUtc <= now);
        await expired.Where(item => db.DocumentEmbeddingBatches.Any(batch => batch.JobId == item.Id
                && batch.TenantId == item.TenantId && batch.Status == DocumentIndexingStates.Submitting))
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, DocumentIndexingStates.ReconciliationRequired)
                .SetProperty(item => item.ClaimId, (Guid?)null).SetProperty(item => item.FinishedAtUtc, now), token);
        await expired.ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, DocumentIndexingStates.Queued)
            .SetProperty(item => item.ClaimId, (Guid?)null), token);
        var job = await db.DocumentIndexingJobs.Where(item => item.Status == DocumentIndexingStates.Queued)
            .OrderBy(item => item.AcceptedAtUtc).ThenBy(item => item.Id).FirstOrDefaultAsync(token);
        if (job is null)
        {
            await transaction.CommitAsync(token);
            return null;
        }
        var manifest = await CurrentManifestAsync(db, job, token);
        if (manifest is null)
        {
            job.Status = DocumentIndexingStates.Revoked;
            job.FinishedAtUtc = now;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return null;
        }
        job.ClaimId = Guid.NewGuid();
        job.ClaimExpiresAtUtc = now.AddSeconds(config.Value.ProcessingTimeoutSeconds);
        job.Status = DocumentIndexingStates.Processing;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(job.Id, job.ClaimId.Value, job.ClaimExpiresAtUtc.Value, manifest);
    }

    private async Task<DocumentIndexingManifest?> CurrentManifestAsync(
        AgentRuntimeDbContext db, DocumentIndexingJobEntity job, CancellationToken token)
    {
        if (job.ManifestJson is null || !config.Value.Enabled)
            return null;
        var persisted = JsonSerializer.Deserialize<DocumentIndexingManifest>(job.ManifestJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The saved indexing manifest is empty.");
        using var json = JsonDocument.Parse(job.ManifestJson);
        if (json.RootElement.GetProperty("version").GetInt32() != persisted.Version
            || json.RootElement.GetProperty("inputVersion").GetString() != persisted.InputVersion
            || DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(persisted, JsonSerializerOptions.Web)) != job.ManifestSha256
            || persisted.TenantId != job.TenantId || persisted.CollectionId != job.CollectionId || persisted.DocumentId != job.DocumentId
            || persisted.JobId != job.PreparationJobId || persisted.GenerationId != job.GenerationId
            || persisted.Policy.Revision != job.PolicyRevision || PolicyHash(persisted) != job.PolicySha256)
            throw new InvalidDataException("The saved indexing manifest has inconsistent identity or ownership.");
        if (await HasNewerPolicyAsync(db, persisted, job.PolicySha256, token))
            return null;
        var pair = await db.DocumentIngestionJobs.AsNoTracking()
            .Where(item => item.Id == job.PreparationJobId && item.TenantId == job.TenantId
                && item.CollectionId == job.CollectionId && item.DocumentId == job.DocumentId)
            .Join(db.KnowledgeDocuments, preparation => preparation.DocumentId, document => document.Id,
                (preparation, document) => new { Preparation = preparation, Document = document })
            .SingleOrDefaultAsync(token);
        if (pair is null)
            return null;
        var source = new DocumentIngestionClaim
        {
            JobId = pair.Preparation.Id,
            ClaimId = Guid.Empty,
            TenantId = job.TenantId,
            CollectionId = job.CollectionId,
            DocumentId = job.DocumentId,
            BlobUri = pair.Document.BlobUri,
            ETag = pair.Document.ETag,
            Sequencer = pair.Preparation.Sequencer,
            PreparationPlanJson = pair.Preparation.PreparationPlanJson,
            ExpiresAtUtc = DateTimeOffset.MinValue,
        };
        var review = await markdownStore.ReadIndexingReviewAsync(db, source, job.GenerationId, token);
        if (review is null)
            return null;
        var current = DocumentIndexingPlanBuilder.Build(config.Value, review);
        return current.Response.ManifestSha256 == job.ManifestSha256 ? current.Manifest : null;
    }

    private async Task<DocumentIndexingJobResponse> ResponseAsync(AgentRuntimeDbContext db, DocumentIndexingJobEntity job, CancellationToken token)
    {
        var manifest = await CurrentManifestAsync(db, job, token);
        var active = manifest is not null && (await ActiveAsync(db, manifest, token))?.JobId == job.Id;
        return Response(job, manifest is not null, active && job.Status == DocumentIndexingStates.Published);
    }

    private static DocumentIndexingJobResponse Response(DocumentIndexingJobEntity job, bool current, bool active) => new()
    {
        Id = job.Id,
        State = job.Status,
        IsCurrent = current,
        IsActive = active,
        VectorCount = job.VectorCount,
        SubmittedRequests = job.SubmittedRequests,
        ReceivedInputTokens = job.ReceivedInputTokens,
    };

    private static string PolicyHash(DocumentIndexingManifest manifest) =>
        DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(new { manifest.Policy, manifest.Profile }, JsonSerializerOptions.Web));

    private static Task<bool> HasNewerPolicyAsync(AgentRuntimeDbContext db, DocumentIndexingManifest manifest, string hash, CancellationToken token) =>
        db.DocumentIndexingJobs.AnyAsync(item => item.TenantId == manifest.TenantId && item.CollectionId == manifest.CollectionId
            && (item.PolicyRevision > manifest.Policy.Revision || item.PolicyRevision == manifest.Policy.Revision && item.PolicySha256 != hash), token);

    private static Task<ActiveDocumentIndexEntity?> ActiveAsync(AgentRuntimeDbContext db, DocumentIndexingManifest manifest, CancellationToken token) =>
        db.ActiveDocumentIndexes.SingleOrDefaultAsync(item => item.TenantId == manifest.TenantId
            && item.CollectionId == manifest.CollectionId && item.DocumentId == manifest.DocumentId, token);
}
#endif
