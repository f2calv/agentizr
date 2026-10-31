#if DEBUG
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

internal sealed partial class DocumentIndexingStore
{
    public async Task<DocumentEmbeddingBatch?> ReserveAsync(DocumentIndexingClaim claim, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        var job = await OwnedAsync(db, claim, now, token);
        if (job is null)
            return null;
        if (await CurrentManifestAsync(db, job, token) is null)
        {
            job.Status = DocumentIndexingStates.Revoked;
            job.ClaimId = null;
            job.FinishedAtUtc = now;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return null;
        }
        var inputs = claim.Manifest.Inputs.Skip(job.VectorCount).Take(claim.Manifest.Profile.MaximumBatchInputs).ToArray();
        if (inputs.Length == 0)
            throw new InvalidDataException("A completed generation cannot submit an additional batch.");
        if (job.SubmittedRequests >= job.MaximumRequests
            || job.ReceivedInputTokens + claim.Manifest.Inputs.Skip(job.VectorCount).Sum(input => (long)input.Chunk.TokenCount) > job.MaximumInputTokens)
        {
            job.Status = DocumentIndexingStates.Failed;
            job.ClaimId = null;
            job.FinishedAtUtc = now;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return null;
        }
        if (await db.DocumentEmbeddingBatches.AnyAsync(item => item.TenantId == job.TenantId
                && item.JobId == job.Id && item.Status == DocumentIndexingStates.Submitting, token))
            throw new InvalidOperationException("The indexing request has no remaining authorized submission.");
        var batch = new DocumentEmbeddingBatch(claim, job.CompletedBatches, inputs);
        db.DocumentEmbeddingBatches.Add(new()
        {
            TenantId = job.TenantId,
            JobId = job.Id,
            Ordinal = batch.Ordinal,
            ClaimId = claim.ClaimId,
            InputSha256 = BatchHash(batch),
            Status = DocumentIndexingStates.Submitting,
            SubmittedAtUtc = now,
        });
        job.SubmittedRequests++;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return batch;
    }

    public async Task<bool> ReceiveAsync(DocumentEmbeddingBatch batch, DocumentEmbeddingResult result, CancellationToken token)
    {
        ValidateResult(batch, result);
        var resultHash = DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var job = await db.DocumentIndexingJobs.SingleAsync(item => item.Id == batch.Claim.JobId
            && item.TenantId == batch.Claim.Manifest.TenantId, token);
        if (DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(batch.Claim.Manifest, JsonSerializerOptions.Web)) != job.ManifestSha256)
            throw new InvalidDataException("The response claim differs from the authorized manifest.");
        var record = await db.DocumentEmbeddingBatches.SingleAsync(item => item.JobId == job.Id
            && item.TenantId == job.TenantId && item.Ordinal == batch.Ordinal, token);
        if (record.ClaimId != batch.Claim.ClaimId || record.InputSha256 != BatchHash(batch))
            throw new InvalidDataException("The embedding response differs from its reserved intent.");
        if (record.Status == DocumentIndexingStates.Received)
        {
            if (record.ResultSha256 != resultHash)
                throw new InvalidDataException("Embedding response receipts cannot be overwritten.");
            return job.Status is DocumentIndexingStates.Queued or DocumentIndexingStates.Published;
        }
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        record.Status = DocumentIndexingStates.Received;
        record.InputTokens = result.InputTokens;
        record.ResultSha256 = resultHash;
        record.ReceivedAtUtc = now;
        job.ReceivedInputTokens = checked(job.ReceivedInputTokens + result.InputTokens);
        var owned = job.ClaimId == batch.Claim.ClaimId && job.Status == DocumentIndexingStates.Processing && job.ClaimExpiresAtUtc > now;
        var eligible = owned && await CurrentManifestAsync(db, job, token) is not null;
        if (!eligible || job.ReceivedInputTokens > job.MaximumInputTokens)
        {
            if (job.Status != DocumentIndexingStates.Revoked)
                job.Status = eligible ? DocumentIndexingStates.Failed : DocumentIndexingStates.ReconciliationRequired;
            job.ClaimId = null;
            job.FinishedAtUtc = now;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return false;
        }
        await DocumentVectorStore.StageAsync(db, batch, result, token);
        job.CompletedBatches++;
        job.VectorCount += batch.Inputs.Count;
        job.ClaimId = null;
        if (job.VectorCount == batch.Claim.Manifest.Inputs.Count)
        {
            await DocumentVectorStore.ValidateAsync(db, batch.Claim, token);
            var active = await ActiveAsync(db, batch.Claim.Manifest, token);
            if (active?.JobId != job.ExpectedActiveJobId)
                job.Status = DocumentIndexingStates.Conflict;
            else
            {
                if (active is null)
                    db.ActiveDocumentIndexes.Add(new()
                    {
                        TenantId = job.TenantId,
                        CollectionId = job.CollectionId,
                        DocumentId = job.DocumentId,
                        JobId = job.Id,
                        ActivatedAtUtc = now,
                    });
                else
                {
                    active.JobId = job.Id;
                    active.ActivatedAtUtc = now;
                }
                job.Status = DocumentIndexingStates.Published;
            }
            job.FinishedAtUtc = now;
        }
        else
            job.Status = DocumentIndexingStates.Queued;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return job.Status is DocumentIndexingStates.Queued or DocumentIndexingStates.Published;
    }

    public async Task FailAsync(DocumentIndexingClaim claim, bool submitted, bool interrupted, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await DocumentIngestionTransactions.LockAsync(db, token);
        var job = await db.DocumentIndexingJobs.SingleOrDefaultAsync(item => item.Id == claim.JobId
            && item.TenantId == claim.Manifest.TenantId && item.ClaimId == claim.ClaimId
            && item.Status == DocumentIndexingStates.Processing, token);
        if (job is null)
            return;
        var unresolvedIntent = submitted || await db.DocumentEmbeddingBatches.AnyAsync(item => item.TenantId == job.TenantId
            && item.JobId == job.Id && item.ClaimId == claim.ClaimId && item.Status == DocumentIndexingStates.Submitting, token);
        job.Status = unresolvedIntent ? DocumentIndexingStates.ReconciliationRequired
            : interrupted ? DocumentIndexingStates.Queued : DocumentIndexingStates.Failed;
        job.ClaimId = null;
        job.FinishedAtUtc = job.Status == DocumentIndexingStates.Queued ? null
            : await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, token);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private static Task<DocumentIndexingJobEntity?> OwnedAsync(
        AgentRuntimeDbContext db, DocumentIndexingClaim claim, DateTimeOffset now, CancellationToken token)
    {
        var hash = DocumentPreparationIdentity.Hash(JsonSerializer.Serialize(claim.Manifest, JsonSerializerOptions.Web));
        return db.DocumentIndexingJobs.SingleOrDefaultAsync(item => item.Id == claim.JobId && item.TenantId == claim.Manifest.TenantId
            && item.ManifestSha256 == hash && item.ClaimId == claim.ClaimId
            && item.Status == DocumentIndexingStates.Processing && item.ClaimExpiresAtUtc > now, token);
    }

    private static string BatchHash(DocumentEmbeddingBatch batch) =>
        DocumentPreparationIdentity.Digest(batch.Claim.Manifest.TenantId, batch.Claim.JobId,
            batch.Claim.ClaimId.ToString(), batch.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(batch.Inputs.Select(input => input.InputSha256)));

    private static void ValidateResult(DocumentEmbeddingBatch batch, DocumentEmbeddingResult result)
    {
        if (result.InputTokens < 0 || result.Vectors.Count != batch.Inputs.Count
            || result.Vectors.Any(vector => vector is null || vector.Length != batch.Claim.Manifest.Profile.Dimensions
                || vector.Any(value => !float.IsFinite(value)) || !vector.Any(value => value != 0)))
            throw new InvalidDataException("Embedding results must contain complete finite nonzero vectors with the pinned dimensions and valid native usage.");
    }
}
#endif
