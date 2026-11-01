using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Commits complete immutable preparation output under the admission/claim lock, without searchable activation.</summary>
internal sealed class DocumentPreparationStoreService(
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory)
{
    /// <summary>Stages once per source/profile; false means the claim is no longer current and no output is committed.</summary>
    public async Task<bool> StoreAsync(
        DocumentIngestionClaim claim,
        PdfPreparationResult preparation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Status != PdfPreparationStatus.Prepared || preparation.Chunks.Count == 0
            || preparation.SourceSha256 is null || preparation.SourceSha256.Length != 64
            || string.IsNullOrWhiteSpace(claim.ETag))
            throw new ArgumentException("Only complete prepared output with a source digest and entity tag can be staged.", nameof(preparation));
        var id = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            claim.TenantId, claim.CollectionId, claim.DocumentId, claim.Sequencer, claim.ETag, preparation.Profile.Id,
        })));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DocumentIngestionTransactions.LockAsync(db, cancellationToken);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken);
        if (!await DocumentIngestionTransactions.CurrentClaim(db, claim, now).AnyAsync(cancellationToken))
            return false;
        if (await db.DocumentPreparationGenerations.AnyAsync(item => item.TenantId == claim.TenantId
            && item.CollectionId == claim.CollectionId && item.DocumentId == claim.DocumentId
            && item.Sequencer == claim.Sequencer && item.ETag == claim.ETag
            && item.SourceSha256 != preparation.SourceSha256, cancellationToken))
            throw new ArgumentException("A prepared source identity was reused with different original bytes.", nameof(preparation));
        if (!await db.DocumentPreparationGenerations.AnyAsync(item => item.Id == id, cancellationToken))
        {
            db.DocumentPreparationGenerations.Add(new DocumentPreparationGenerationEntity
            {
                Id = id,
                TenantId = claim.TenantId,
                CollectionId = claim.CollectionId,
                DocumentId = claim.DocumentId,
                Sequencer = claim.Sequencer,
                ETag = claim.ETag,
                SourceSha256 = preparation.SourceSha256,
                ProfileId = preparation.Profile.Id,
                ProfileJson = JsonSerializer.Serialize(preparation.Profile),
                PageCount = preparation.PageCount,
                PagesWithoutTextJson = JsonSerializer.Serialize(preparation.PagesWithoutText),
                ChunkCount = preparation.Chunks.Count,
                PreparedAtUtc = now,
            });
            db.DocumentTextChunks.AddRange(preparation.Chunks.Select(chunk => new DocumentTextChunkEntity
            {
                GenerationId = id,
                TenantId = claim.TenantId,
                CollectionId = claim.CollectionId,
                Ordinal = chunk.Ordinal,
                PageNumber = chunk.PageNumber,
                StartOffset = chunk.StartOffset,
                EndOffset = chunk.EndOffset,
                TokenCount = chunk.TokenCount,
                Text = chunk.Text,
            }));
            await db.SaveChangesAsync(cancellationToken);
        }
        now = await DocumentIngestionTransactions.GetUtcNowAsync(db, timeProvider, cancellationToken);
        if (!await DocumentIngestionTransactions.CurrentClaim(db, claim, now).AnyAsync(cancellationToken))
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
