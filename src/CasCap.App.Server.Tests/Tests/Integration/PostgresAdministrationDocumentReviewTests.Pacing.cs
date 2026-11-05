using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Fact]
    public async Task Indexing_ConcurrentJobsCanReserveOnlyOneDeploymentIntent()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            await AcceptNextIndexingGenerationAsync(client, request);
            var firstStore = IndexStore(settings);
            var secondStore = IndexStore(settings);
            var first = Assert.IsType<DocumentIndexingClaim>(await firstStore.ClaimAsync(Token));
            var second = Assert.IsType<DocumentIndexingClaim>(await secondStore.ClaimAsync(Token));
            Assert.NotEqual(first.JobId, second.JobId);
            var reservations = await Task.WhenAll(firstStore.ReserveAsync(first, Token), secondStore.ReserveAsync(second, Token));
            var reserved = Assert.Single(reservations.OfType<DocumentEmbeddingBatch>());
            await using var db = _database.DbContextFactory.CreateDbContext();
            Assert.Single(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            var jobs = await db.DocumentIndexingJobs.AsNoTracking().ToArrayAsync(Token);
            Assert.Equal(1, jobs.Sum(job => job.SubmittedRequests));
            var waiting = Assert.Single(jobs, job => job.Status == DocumentIndexingStates.Queued);
            Assert.Null(waiting.ClaimId);
            Assert.Equal(0, waiting.SubmittedRequests);
            Assert.Equal(request.MaximumRequests, waiting.MaximumRequests);
            await firstStore.FailAsync(reserved.Claim, submitted: true, interrupted: false, Token);
            Assert.Null(await IndexStore(settings).ClaimAsync(Token));
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
        }
    }

    [Fact]
    public async Task Indexing_PacingSurvivesFreshStoreWithoutConsumingAuthorizationOrVerifyingProvider()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var provider = new EmbeddingProvider();
            Assert.True(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            Assert.False(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            Assert.Equal(1, provider.Submissions);
            Assert.Equal(1, provider.Verifications);
            await using var db = _database.DbContextFactory.CreateDbContext();
            var before = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(DocumentIndexingStates.Queued, before.Status);
            Assert.Null(before.ClaimId);
            Assert.Equal(1, before.SubmittedRequests);
            Assert.Single(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => IndexStore(settings).ClaimAsync(cancelled.Token));
            await ExpireEmbeddingPacingAsync();
            Assert.True(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            var after = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(2, after.SubmittedRequests);
            Assert.Equal(2, after.VectorCount);
            Assert.Equal(2, provider.Submissions);
            Assert.Equal(request.MaximumRequests, after.MaximumRequests);
            Assert.Equal(request.MaximumInputTokens, after.MaximumInputTokens);
        }
    }

    [Fact]
    public async Task Indexing_ReservationRechecksPacingAndReleasesClaimWithoutAnotherIntent()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var provider = new EmbeddingProvider();
            Assert.True(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            await ExpireEmbeddingPacingAsync();
            var store = IndexStore(settings);
            var claim = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
            await db.DocumentEmbeddingBatches.ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.ReceivedAtUtc, now), Token);
            Assert.Null(await store.ReserveAsync(claim, Token));
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(DocumentIndexingStates.Queued, job.Status);
            Assert.Null(job.ClaimId);
            Assert.Null(job.ClaimExpiresAtUtc);
            Assert.Equal(1, job.SubmittedRequests);
            Assert.Single(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            Assert.Equal(1, provider.Submissions);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indexing_PacingScopesAliasesAndRetainedRetirementAudit(bool retired)
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            Assert.True(await Processor(IndexStore(settings), new(), settings).ProcessOneAsync(Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            var manifest = JsonSerializer.Deserialize<DocumentIndexingManifest>(job.ManifestJson!, JsonSerializerOptions.Web)!;
            var receipt = await db.DocumentEmbeddingBatches.AsNoTracking().SingleAsync(Token);
            await using var transaction = await db.Database.BeginTransactionAsync(Token);
            await DocumentIngestionTransactions.LockAsync(db, Token);
            if (retired)
            {
                var source = await db.KnowledgeDocuments.SingleAsync(Token);
                source.Status = DocumentIngestionStates.Deleted;
                await DocumentSourceRetirement.PurgeAsync(db, source, Token);
                await db.SaveChangesAsync(Token);
            }
            var alias = manifest with
            {
                TenantId = "other-tenant",
                Profile = manifest.Profile with
                {
                    Id = "other-profile",
                    Provider = "other-provider",
                    Azure = manifest.Profile.Azure! with
                    {
                        Endpoint = "https://alias.example.com/",
                        DeploymentResourceId = manifest.Profile.Azure.DeploymentResourceId.ToUpperInvariant(),
                    },
                },
            };
            Assert.True(await DocumentEmbeddingPacing.IsPendingAsync(db, settings, alias, receipt.ReceivedAtUtc!.Value.AddSeconds(64), Token));
            Assert.False(await DocumentEmbeddingPacing.IsPendingAsync(db, settings, alias, receipt.ReceivedAtUtc.Value.AddSeconds(65), Token));
            var otherDeployment = alias with
            {
                Profile = alias.Profile with { Azure = alias.Profile.Azure! with { DeploymentResourceId = alias.Profile.Azure.DeploymentResourceId + "-other" } },
            };
            Assert.Equal(retired, await DocumentEmbeddingPacing.IsPendingAsync(db, settings, otherDeployment, receipt.ReceivedAtUtc.Value, Token));
            Assert.False(await DocumentEmbeddingPacing.IsPendingAsync(db, settings,
                manifest with { Profile = DocumentIndexingTestData.OllamaConfig().Profiles[0] }, receipt.ReceivedAtUtc.Value, Token));
            await transaction.CommitAsync(Token);
        }
    }

    [Fact]
    public async Task Indexing_UnresolvedIntentUsesClaimDeadlineButNeverReplays()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var provider = new EmbeddingProvider { Fail = true };
            Assert.True(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            var manifest = JsonSerializer.Deserialize<DocumentIndexingManifest>(job.ManifestJson!, JsonSerializerOptions.Web)!;
            var intent = await db.DocumentEmbeddingBatches.AsNoTracking().SingleAsync(Token);
            Assert.True(await DocumentEmbeddingPacing.IsPendingAsync(db, settings, manifest, intent.SubmittedAtUtc.AddSeconds(66), Token));
            Assert.False(await DocumentEmbeddingPacing.IsPendingAsync(db, settings, manifest, job.ClaimExpiresAtUtc!.Value.AddSeconds(65), Token));
            Assert.Equal(DocumentIndexingStates.ReconciliationRequired, job.Status);
            Assert.Null(intent.ReceivedAtUtc);
            Assert.False(await Processor(IndexStore(settings), provider, settings).ProcessOneAsync(Token));
            Assert.Equal(1, provider.Submissions);
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
        }
    }
}
