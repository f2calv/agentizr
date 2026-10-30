#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Fact]
    public async Task Indexing_DurableAcceptanceAndNativeVectorsPublishOnlyWhenComplete()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            var accepted = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.RequestDocumentIndexingAsync(
                "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id, request, Token)));
            Assert.All(accepted, item => Assert.True(item.IsSuccess));
            var jobId = Assert.Single(accepted.Select(item => item.Value!.Id).Distinct());
            var conflict = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request with { MaximumRequests = request.MaximumRequests + 1 }, Token);
            Assert.Equal(ApiStatus.Conflict, conflict.Status);
            var store = IndexStore(settings);
            var provider = new EmbeddingProvider();
            await using var db = _database.DbContextFactory.CreateDbContext();
            var seen = 0;
            while (await Processor(store, provider, settings).ProcessOneAsync(Token))
            {
                seen++;
                var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
                var count = await VectorCountAsync(db);
                Assert.Equal(seen, count);
                if (job.Status == DocumentIndexingStates.Published)
                    break;
                Assert.Equal(DocumentIndexingStates.Queued, job.Status);
                Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
                Assert.True(seen < 100);
            }
            Assert.True(seen > 1);
            Assert.Equal(seen, provider.Submissions);
            Assert.Equal(seen, await db.DocumentEmbeddingBatches.CountAsync(Token));
            Assert.All(await db.DocumentEmbeddingBatches.ToArrayAsync(Token), item => Assert.Equal(DocumentIndexingStates.Received, item.Status));
            Assert.Equal(jobId, (await db.ActiveDocumentIndexes.SingleAsync(Token)).JobId);
            var status = await client.GetDocumentIndexingJobAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, jobId, Token);
            Assert.True(status.IsSuccess);
            Assert.True(status.Value!.IsActive);
            Assert.True(status.Value.IsCurrent);
            Assert.Equal(seen, status.Value.VectorCount);
            var observed = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, Token);
            Assert.Equal(jobId, observed.Value!.ObservedActiveJobId);
            Assert.False(observed.Value.IndexingApproved);
            Assert.False(await Processor(store, provider, settings).ProcessOneAsync(Token));
            Assert.Equal(seen, provider.Submissions);
            var vectorText = await db.Database.SqlQuery<string>($"""
                SELECT embedding::text AS "Value" FROM agent_runtime.document_index_vectors WHERE job_id = {jobId} ORDER BY ordinal LIMIT 1
                """).SingleAsync(Token);
            Assert.Equal("[1,2,3,4,5,6,7,8]", vectorText);
            Assert.Equal(1, _provider.ModelCalls);
            await using (var transaction = await db.Database.BeginTransactionAsync(Token))
            {
                await DocumentIngestionTransactions.LockAsync(db, Token);
                var source = await db.KnowledgeDocuments.SingleAsync(Token);
                source.Status = DocumentIngestionStates.Deleted;
                await DocumentSourceRetirement.PurgeAsync(db, source, Token);
                await db.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
            }
            Assert.Equal(0, await VectorCountAsync(db));
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
            var retired = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Null(retired.ManifestJson);
            Assert.Equal(DocumentIndexingStates.Revoked, retired.Status);
            Assert.Equal(seen, retired.SubmittedRequests);
            Assert.Equal(seen, await db.DocumentEmbeddingBatches.CountAsync(Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indexing_ExpiredClaimsNeverReplaySubmittedUnknownOutcomes(bool submitted)
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var store = IndexStore(settings);
            var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.ClaimAsync(Token)));
            var claim = Assert.Single(claims.OfType<DocumentIndexingClaim>());
            if (submitted)
                Assert.NotNull(await store.ReserveAsync(claim, Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            await db.DocumentIndexingJobs.ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.ClaimExpiresAtUtc, DateTimeOffset.UnixEpoch), Token);
            var resumed = await store.ClaimAsync(Token);
            if (submitted)
            {
                Assert.Null(resumed);
                Assert.Equal(DocumentIndexingStates.ReconciliationRequired, (await db.DocumentIndexingJobs.SingleAsync(Token)).Status);
                Assert.Single(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            }
            else
            {
                Assert.NotNull(resumed);
                Assert.NotEqual(claim.ClaimId, resumed.ClaimId);
                Assert.Empty(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            }
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
            Assert.Equal(0, await VectorCountAsync(db));
        }
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("purge")]
    [InlineData("timeout")]
    [InlineData("policy")]
    [InlineData("budget")]
    public async Task Indexing_LateOrObsoleteReceiptsRetainUsageButCannotActivate(string change)
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            var accepted = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token);
            Assert.True(accepted.IsSuccess);
            var store = IndexStore(settings);
            var claim = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
            var batch = Assert.IsType<DocumentEmbeddingBatch>(await store.ReserveAsync(claim, Token));
            Task<bool>? pendingReceipt = null;
            await using var db = _database.DbContextFactory.CreateDbContext();
            await using (var transaction = await db.Database.BeginTransactionAsync(Token))
            {
                await DocumentIngestionTransactions.LockAsync(db, Token);
                if (change is "selection" or "source")
                {
                    pendingReceipt = store.ReceiveAsync(batch, EmbeddingProvider.Result(batch), Token);
                    await _database.WaitForBlockedAdvisoryLockAsync(Token);
                }
                if (change == "selection")
                {
                    var selection = await db.DocumentChunkReviews.SingleAsync(Token);
                    db.DocumentChunkReviews.Add(new()
                    {
                        TenantId = selection.TenantId,
                        GenerationId = id,
                        Revision = 2,
                        ActorId = "example-reviewer",
                        ExclusionsJson = selection.ExclusionsJson,
                        RecordedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
                else if (change is "source" or "purge")
                {
                    var source = await db.KnowledgeDocuments.SingleAsync(Token);
                    source.Status = DocumentIngestionStates.Deleted;
                    if (change == "purge")
                        await DocumentSourceRetirement.PurgeAsync(db, source, Token);
                }
                else if (change == "timeout")
                    await db.DocumentIndexingJobs.ExecuteUpdateAsync(update =>
                        update.SetProperty(item => item.ClaimExpiresAtUtc, DateTimeOffset.UnixEpoch), Token);
                await db.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
            }
            if (change == "policy")
            {
                var next = settings with { Collections = [settings.Collections[0] with { Revision = 2 }] };
                var otherStore = IndexStore(next);
                var review = await new DocumentPreparationMarkdownStore(
                    Options.Create(new DocumentPreparationConfig()), TimeProvider.System,
                    _database.DbContextFactory, new()).PreflightIndexingAsync(_source, id, next, Token);
                var other = await otherStore.AcceptAsync(_source, id, request with
                {
                    ManifestSha256 = review!.Response.ManifestSha256!,
                }, "example-operator", Token);
                Assert.NotNull(other.Job);
            }
            var usage = change == "budget" ? request.MaximumInputTokens + 1 : 7;
            Assert.False(pendingReceipt is null
                ? await store.ReceiveAsync(batch, EmbeddingProvider.Result(batch, usage), Token) : await pendingReceipt);
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(item => item.Id == claim.JobId, Token);
            Assert.Equal(usage, job.ReceivedInputTokens);
            Assert.Equal(DocumentIndexingStates.Received, (await db.DocumentEmbeddingBatches.SingleAsync(Token)).Status);
            Assert.Equal(0, await VectorCountAsync(db));
            Assert.DoesNotContain(await db.ActiveDocumentIndexes.ToArrayAsync(Token), item => item.JobId == claim.JobId);
            if (change == "purge")
            {
                Assert.Null(job.ManifestJson);
                Assert.Equal(DocumentIndexingStates.Revoked, job.Status);
            }
        }
    }

    [Theory]
    [InlineData("confirmation", ApiStatus.ValidationFailed)]
    [InlineData("manifest", ApiStatus.Conflict)]
    [InlineData("tokens", ApiStatus.ValidationFailed)]
    [InlineData("requests", ApiStatus.ValidationFailed)]
    [InlineData("predecessor", ApiStatus.Conflict)]
    public async Task Indexing_RejectsIncompleteOrStaleAuthorizationWithoutIntents(string change, ApiStatus expected)
    {
        var (http, client, id, _, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            request = change switch
            {
                "confirmation" => request with { Confirmed = false },
                "manifest" => request with { ManifestSha256 = new('0', 64) },
                "tokens" => request with { MaximumInputTokens = 1 },
                "requests" => request with { MaximumRequests = 1 },
                _ => request with { ExpectedActiveJobId = "unobserved-generation" },
            };
            var result = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token);
            Assert.Equal(expected, result.Status);
            await using var db = _database.DbContextFactory.CreateDbContext();
            Assert.Empty(await db.DocumentIndexingJobs.ToArrayAsync(Token));
            Assert.Empty(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            Assert.Equal(0, await VectorCountAsync(db));
        }
    }

    [Fact]
    public async Task Indexing_NativeVectorForeignKeyRejectsCrossTenantRows()
    {
        var (http, client, id, _, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            var accepted = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token);
            Assert.True(accepted.IsSuccess);
            await using var db = _database.DbContextFactory.CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(Token);
            var foreignTenant = "other-tenant";
            var hash = new string('0', 64);
            var vector = "[1,2,3,4,5,6,7,8]";
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agent_runtime.document_index_vectors (tenant_id, job_id, ordinal, input_sha256, embedding)
                VALUES ({foreignTenant}, {accepted.Value!.Id}, 0, {hash}, CAST({vector} AS vector))
                """, Token));
            Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            await transaction.RollbackAsync(Token);
            Assert.Equal(0, await VectorCountAsync(db));
        }
    }

    [Fact]
    public async Task Indexing_TransportFailureIsDurablyUnknownAndNeverResubmitted()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var store = IndexStore(settings);
            var provider = new EmbeddingProvider { Fail = true };
            Assert.True(await Processor(store, provider, settings).ProcessOneAsync(Token));
            Assert.False(await Processor(store, provider, settings).ProcessOneAsync(Token));
            Assert.Equal(1, provider.Submissions);
            await using var db = _database.DbContextFactory.CreateDbContext();
            Assert.Equal(DocumentIndexingStates.ReconciliationRequired, (await db.DocumentIndexingJobs.SingleAsync(Token)).Status);
            Assert.Equal(DocumentIndexingStates.Submitting, (await db.DocumentEmbeddingBatches.SingleAsync(Token)).Status);
            Assert.Equal(0, await VectorCountAsync(db));
        }
    }

    [Fact]
    public async Task Indexing_ReceiptRetryIsWriteOnceAndUnsubmittedInterruptionCanResume()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var store = IndexStore(settings);
            var interrupted = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
            await store.FailAsync(interrupted, submitted: false, interrupted: true, Token);
            var claim = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
            Assert.NotEqual(interrupted.ClaimId, claim.ClaimId);
            var batch = Assert.IsType<DocumentEmbeddingBatch>(await store.ReserveAsync(claim, Token));
            var result = EmbeddingProvider.Result(batch);
            Assert.True(await store.ReceiveAsync(batch, result, Token));
            Assert.True(await store.ReceiveAsync(batch, result, Token));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReceiveAsync(batch, result with { InputTokens = 8 }, Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(1, job.SubmittedRequests);
            Assert.Equal(1, job.CompletedBatches);
            Assert.Equal(7, job.ReceivedInputTokens);
            Assert.Equal(1, await VectorCountAsync(db));
        }
    }

    [Fact]
    public async Task Indexing_DurableIntentWinsOverAnUncertainReserveAcknowledgement()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var store = IndexStore(settings);
            var claim = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
            Assert.NotNull(await store.ReserveAsync(claim, Token));
            await store.FailAsync(claim, submitted: false, interrupted: true, Token);
            Assert.Null(await store.ClaimAsync(Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            Assert.Equal(DocumentIndexingStates.ReconciliationRequired, (await db.DocumentIndexingJobs.SingleAsync(Token)).Status);
            Assert.Single(await db.DocumentEmbeddingBatches.ToArrayAsync(Token));
            Assert.Empty(await db.ActiveDocumentIndexes.ToArrayAsync(Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indexing_OwnershipCancellationDistinguishesUnsubmittedAndUnknownPaidWork(bool submitted)
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        using (var ownership = new CancellationTokenSource())
        {
            Assert.True((await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token)).IsSuccess);
            var provider = new EmbeddingProvider
            {
                StopBeforeSubmission = submitted ? null : ownership,
                StopAfterSubmission = submitted ? ownership : null,
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(IndexStore(settings), provider, settings).ProcessOneAsync(ownership.Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            var job = await db.DocumentIndexingJobs.AsNoTracking().SingleAsync(Token);
            Assert.Equal(submitted ? DocumentIndexingStates.ReconciliationRequired : DocumentIndexingStates.Queued, job.Status);
            Assert.Null(job.ClaimId);
            Assert.Equal(submitted ? 1 : 0, provider.Submissions);
            Assert.Equal(submitted ? 1 : 0, await db.DocumentEmbeddingBatches.CountAsync(Token));
        }
    }

    [Fact]
    public async Task Indexing_CompetingCompleteGenerationCannotOverwriteObservedPredecessor()
    {
        var (http, client, id, settings, request) = await AcceptedIndexingInputsAsync();
        using (http)
        {
            var original = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, id, request, Token);
            Assert.True(original.IsSuccess);
            var store = IndexStore(settings);
            DocumentEmbeddingBatch last;
            var staged = 0;
            while (true)
            {
                var claim = Assert.IsType<DocumentIndexingClaim>(await store.ClaimAsync(Token));
                last = Assert.IsType<DocumentEmbeddingBatch>(await store.ReserveAsync(claim, Token));
                if (staged + last.Inputs.Count == claim.Manifest.Inputs.Count)
                    break;
                Assert.True(await store.ReceiveAsync(last, EmbeddingProvider.Result(last), Token));
                staged += last.Inputs.Count;
            }
            var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, Request() with { TargetTokensPerChunk = 128 }, Token);
            Assert.True(stored.IsSuccess);
            var nextId = stored.Value!.GenerationId!;
            Assert.NotEqual(id, nextId);
            Assert.True((await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, nextId, NativeSelectionRequest(), Token)).IsSuccess);
            Assert.True((await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, nextId, NativeApprovalRequest(), Token)).IsSuccess);
            var preflight = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, nextId, Token);
            var next = await client.RequestDocumentIndexingAsync("tenant-a", _source.CollectionId,
                _source.DocumentId, _source.JobId, nextId, request with { ManifestSha256 = preflight.Value!.ManifestSha256! }, Token);
            Assert.True(next.IsSuccess);
            var provider = new EmbeddingProvider();
            while (await Processor(store, provider, settings).ProcessOneAsync(Token))
                Assert.True(provider.Submissions < 100);
            Assert.False(await store.ReceiveAsync(last, EmbeddingProvider.Result(last), Token));
            await using var db = _database.DbContextFactory.CreateDbContext();
            Assert.Equal(next.Value!.Id, (await db.ActiveDocumentIndexes.SingleAsync(Token)).JobId);
            var old = await db.DocumentIndexingJobs.SingleAsync(item => item.Id == original.Value!.Id, Token);
            Assert.Equal(DocumentIndexingStates.Conflict, old.Status);
            Assert.Equal(last.Claim.Manifest.Inputs.Count, old.VectorCount);
        }
    }

    private DocumentIndexingStore IndexStore(DocumentIndexingConfig settings) => new(Options.Create(settings), TimeProvider.System,
        _database.DbContextFactory, new(Options.Create(new DocumentPreparationConfig()),
            TimeProvider.System, _database.DbContextFactory, new()));

    private static DocumentIndexingProcessingService Processor(DocumentIndexingStore store, EmbeddingProvider provider, DocumentIndexingConfig settings) =>
        new(NullLogger<DocumentIndexingProcessingService>.Instance, Options.Create(settings), store, provider);

    private async Task<(HttpClient Http, AdministrationApiClient Client, string Id, DocumentIndexingConfig Settings, DocumentIndexingRequest Request)> AcceptedIndexingInputsAsync()
    {
        var settings = DocumentIndexingTestData.ExecutionConfig(_source.TenantId, _source.CollectionId);
        await _host.DisposeAsync();
        _host = new(_database.DbContextFactory, indexing: settings);
        var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new());
        var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request() with { MaximumTokensPerChunk = 32, TargetTokensPerChunk = 16, OverlapTokens = 0 }, Token);
        Assert.True(stored.IsSuccess);
        var id = stored.Value!.GenerationId!;
        Assert.True((await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, NativeSelectionRequest(), Token)).IsSuccess);
        Assert.True((await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, NativeApprovalRequest(), Token)).IsSuccess);
        var preflight = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.True(preflight.Value!.Eligible);
        Assert.Null(preflight.Value.ObservedActiveJobId);
        Assert.True(preflight.Value.IncludedChunkCount > 1);
        return (http, client, id, settings, new()
        {
            Confirmed = true,
            ManifestSha256 = preflight.Value.ManifestSha256!,
            MaximumRequests = 100,
            MaximumInputTokens = 100_000,
            ExpectedActiveJobId = null,
        });
    }

    private static Task<int> VectorCountAsync(AgentRuntimeDbContext db) =>
        db.Database.SqlQuery<int>($"""SELECT count(*)::integer AS "Value" FROM agent_runtime.document_index_vectors""").SingleAsync(Token);

    private sealed class EmbeddingProvider : IDocumentEmbeddingProvider
    {
        public int Submissions { get; private set; }

        public bool Fail { get; init; }

        public CancellationTokenSource? StopBeforeSubmission { get; init; }

        public CancellationTokenSource? StopAfterSubmission { get; init; }

        public Task VerifyAsync(DocumentIndexingManifest manifest, CancellationToken token)
        {
            StopBeforeSubmission?.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<DocumentEmbeddingResult> GenerateAsync(DocumentEmbeddingBatch batch, CancellationToken token)
        {
            Submissions++;
            StopAfterSubmission?.Cancel();
            token.ThrowIfCancellationRequested();
            if (Fail)
                throw new HttpRequestException("Synthetic unknown transport outcome.");
            return Task.FromResult(Result(batch));
        }

        public static DocumentEmbeddingResult Result(DocumentEmbeddingBatch batch, long usage = 7) =>
            new(batch.Inputs.Select(_ => new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }).ToArray(), usage);
    }
}
#endif
