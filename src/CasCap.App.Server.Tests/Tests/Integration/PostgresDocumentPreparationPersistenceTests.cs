#if DEBUG
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Proves prepared-generation atomicity, source fencing and competing writes against migrated PostgreSQL.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "PreparedDocuments")]
public sealed class PostgresDocumentPreparationPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _database = new();
    private readonly byte[] _bytes = DocumentPreparationTestData.Pdf("Synthetic reference.", "", "Another source page.");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with { ProcessingEnabled = true };

    public ValueTask InitializeAsync() => _database.InitializeAsync();
    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ProcessorCommitsCompleteOutput()
    {
        await Inbox().AcceptAsync(DocumentIngestionTestData.Notification(), Token);
        var processor = new PdfDocumentProcessor(
            DocumentHybridPreparationTestData.Downloader(() => new MemoryStream(_bytes), Config),
            new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), new DocumentChunkingService()), Store());
        var processing = new DocumentIngestionProcessingService(NullLogger<DocumentIngestionProcessingService>.Instance,
            Options.Create(Config), TimeProvider.System, Jobs(), [processor]);
        Assert.True(await processing.ProcessOneAsync(Token));
        await using var db = _database.DbContextFactory.CreateDbContext();
        var generation = await db.DocumentPreparationGenerations.SingleAsync(Token);
        var chunks = await db.DocumentTextChunks.OrderBy(item => item.Ordinal).ToArrayAsync(Token);
        Assert.Equal(DocumentIngestionStates.Completed, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(DocumentIngestionStates.Queued, (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(_bytes)), generation.SourceSha256);
        Assert.Equal(3, generation.PageCount);
        Assert.Equal([2], Assert.IsType<int[]>(JsonSerializer.Deserialize<int[]>(generation.PagesWithoutTextJson)));
        Assert.Equal(generation.ProfileId,
            Assert.IsType<DocumentPreparationProfile>(JsonSerializer.Deserialize<DocumentPreparationProfile>(generation.ProfileJson)).Id);
        Assert.Equal(generation.ChunkCount, chunks.Length);
        Assert.Collection(chunks, chunk => Assert.Equal(1, chunk.PageNumber), chunk => Assert.Equal(3, chunk.PageNumber));
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(generation.Id, chunk.GenerationId);
            Assert.Equal(generation.TenantId, chunk.TenantId);
            Assert.Equal(generation.CollectionId, chunk.CollectionId);
            Assert.Equal(chunk.Text.Length, chunk.EndOffset - chunk.StartOffset);
            Assert.InRange(chunk.TokenCount, 1, 512);
        });
        var types = await db.Database.SqlQuery<string>($"""
            SELECT udt_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'agent_runtime' AND table_name = 'document_preparation_generations'
                AND column_name IN ('profile_json', 'pages_without_text_json')
            """).ToArrayAsync(Token);
        Assert.Equal(2, types.Length);
        Assert.All(types, type => Assert.Equal("jsonb", type));
        Assert.Empty(await db.DocumentPreparationOperations.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ConcurrentGenerationWrites()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Store().StoreAsync(claim, preparation, Token)));
        Assert.All(writes, Assert.True);
        await AssertCountsAsync(1, preparation.Chunks.Count);
        await using var db = _database.DbContextFactory.CreateDbContext();
        var generation = await db.DocumentPreparationGenerations.SingleAsync(Token);
        Assert.Equal(preparation.Profile.Id, generation.ProfileId);
        Assert.Equal(Enumerable.Range(0, preparation.Chunks.Count),
            await db.DocumentTextChunks.OrderBy(chunk => chunk.Ordinal).Select(chunk => chunk.Ordinal).ToArrayAsync(Token));
    }

    [Fact]
    public async Task RetryReusesCommittedGeneration()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        Assert.True(await Store().StoreAsync(claim, preparation, Token));
        await using var db = _database.DbContextFactory.CreateDbContext();
        var before = await db.DocumentPreparationGenerations.AsNoTracking().SingleAsync(Token);
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        await db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.ClaimExpiresAtUtc, now.AddSeconds(-1)), Token);
        Assert.Null(await Jobs().ClaimAsync(Token));
        await db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAtUtc, now.AddSeconds(-1)), Token);
        var retry = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(Token));
        Assert.NotEqual(claim.ClaimId, retry.ClaimId);
        Assert.False(await Store().StoreAsync(claim, preparation, Token));
        Assert.True(await Store().StoreAsync(retry, preparation, Token));
        Assert.True(await Jobs().FinishAsync(retry, DocumentProcessingOutcome.Completed, Token));
        var after = await db.DocumentPreparationGenerations.AsNoTracking().SingleAsync(Token);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PreparedAtUtc, after.PreparedAtUtc);
        Assert.Equal(before.SourceSha256, after.SourceSha256);
        await AssertCountsAsync(1, preparation.Chunks.Count);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("tenant")]
    [InlineData("collection")]
    [InlineData("sequencer")]
    [InlineData("blob")]
    [InlineData("etag")]
    [InlineData("claim")]
    [InlineData("plan")]
    public async Task ClaimFences(string changed)
    {
        var claim = await ClaimAsync();
        var forged = changed switch
        {
            "document" => claim with { DocumentId = new string('B', 64) },
            "tenant" => claim with { TenantId = "tenant-b" },
            "collection" => claim with { CollectionId = "other" },
            "sequencer" => claim with { Sequencer = "2".PadLeft(128, '0') },
            "blob" => claim with { BlobUri = "https://storage.example.com/documents/another.pdf" },
            "etag" => claim with { ETag = "different-etag" },
            "claim" => claim with { ClaimId = Guid.NewGuid() },
            "plan" => claim with { PreparationPlanJson = "{}" },
            _ => throw new ArgumentOutOfRangeException(nameof(changed)),
        };
        Assert.False(await Store().StoreAsync(forged, await PrepareAsync(), Token));
        Assert.False(await Jobs().FinishAsync(forged, DocumentProcessingOutcome.Completed, Token));
        await AssertCountsAsync(0, 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AdmissionAndGenerationSerialize(bool deleted, bool admissionFirst)
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new SaveCheckpoint(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
        });
        var next = DocumentIngestionTestData.Notification("next-event", "2", deleted);
        Task<bool>? saving = null;
        Task? admitting = null;
        try
        {
            if (admissionFirst)
                admitting = Inbox(checkpoint).AcceptAsync(next, Token);
            else
                saving = Store(checkpoint).StoreAsync(claim, preparation, Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            if (admissionFirst)
                saving = Store().StoreAsync(claim, preparation, Token);
            else
                admitting = Inbox().AcceptAsync(next, Token);
            await WaitForBlockedAdvisoryLockAsync();
            Assert.NotNull(admitting);
            Assert.NotNull(saving);
            Assert.False(admitting.IsCompleted);
            Assert.False(saving.IsCompleted);
            await AssertCountsAsync(0, 0);
            release.SetResult();
            await admitting;
            Assert.Equal(!admissionFirst, await saving);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(saving ?? Task.CompletedTask, admitting ?? Task.CompletedTask);
        }
        Assert.False(await Store().StoreAsync(claim, preparation, Token));
        Assert.False(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.Completed, Token));
        await AssertCountsAsync(admissionFirst ? 0 : 1, admissionFirst ? 0 : preparation.Chunks.Count);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(deleted ? DocumentIngestionStates.Deleted : DocumentIngestionStates.Queued,
            (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Equal(DocumentIngestionStates.Ignored,
            (await db.DocumentIngestionJobs.SingleAsync(job => job.Id == claim.JobId, Token)).Status);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancellation")]
    [InlineData("expiry")]
    public async Task RollbackAfterInsert(string reason)
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var checkpoint = new SaveCheckpoint(async (db, cancellationToken) =>
        {
            Assert.Equal(1, await db.DocumentPreparationGenerations.CountAsync(cancellationToken));
            Assert.Equal(preparation.Chunks.Count, await db.DocumentTextChunks.CountAsync(cancellationToken));
            await AssertCountsAsync(0, 0);
            if (reason == "failure")
                throw new IOException("Synthetic post-insert failure.");
            if (reason == "cancellation")
                await cancellation.CancelAsync();
            if (reason == "expiry")
            {
                var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, cancellationToken);
                await db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
                    .ExecuteUpdateAsync(update => update.SetProperty(job => job.ClaimExpiresAtUtc, now.AddSeconds(-1)), cancellationToken);
            }
        });
        var store = Store(checkpoint);
        if (reason == "failure")
            await Assert.ThrowsAsync<IOException>(() => store.StoreAsync(claim, preparation, cancellation.Token));
        else if (reason == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StoreAsync(claim, preparation, cancellation.Token));
        else
            Assert.False(await store.StoreAsync(claim, preparation, cancellation.Token));
        await AssertCountsAsync(0, 0);
        Assert.True(await Store().StoreAsync(claim, preparation, Token));
        await AssertCountsAsync(1, preparation.Chunks.Count);
    }

    [Fact]
    public async Task AdvisoryLockCancellation()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        await using var owner = _database.DbContextFactory.CreateDbContext();
        await using var held = await owner.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(owner, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = Store().StoreAsync(claim, preparation, cancellation.Token);
        try
        {
            await WaitForBlockedAdvisoryLockAsync();
            Assert.False(pending.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                await pending;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            await held.RollbackAsync(Token);
        }
        await AssertCountsAsync(0, 0);
        Assert.True(await Store().StoreAsync(claim, preparation, Token));
        await AssertCountsAsync(1, preparation.Chunks.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChunkOwnershipConstraint(bool tenant)
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        Assert.True(await Store().StoreAsync(claim, preparation, Token));
        await using var db = _database.DbContextFactory.CreateDbContext();
        var chunk = await db.DocumentTextChunks.FirstAsync(Token);
        if (tenant)
            chunk.TenantId = "tenant-b";
        else
            chunk.CollectionId = "other";
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        await using var verification = _database.DbContextFactory.CreateDbContext();
        var generation = await verification.DocumentPreparationGenerations.SingleAsync(Token);
        Assert.All(await verification.DocumentTextChunks.ToArrayAsync(Token), saved =>
        {
            Assert.Equal(generation.TenantId, saved.TenantId);
            Assert.Equal(generation.CollectionId, saved.CollectionId);
        });
    }

    [Fact]
    public async Task ConcurrentProfilesStaySeparate()
    {
        var claim = await ClaimAsync();
        var first = await PrepareAsync();
        var second = await PrepareAsync(new DocumentChunkingConfig { MaximumTokensPerChunk = 128, OverlapTokens = 16 });
        Assert.NotEqual(first.Profile.Id, second.Profile.Id);
        var writes = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => Store().StoreAsync(claim, index % 2 == 0 ? first : second, Token)));
        Assert.All(writes, Assert.True);
        await AssertCountsAsync(2, first.Chunks.Count + second.Chunks.Count);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(2, await db.DocumentPreparationGenerations.Select(generation => generation.ProfileId).Distinct().CountAsync(Token));
        Assert.Single(await db.DocumentPreparationGenerations.Select(generation => generation.SourceSha256).Distinct().ToArrayAsync(Token));
    }

    [Fact]
    public async Task CompetingOriginalDigests()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        var changed = preparation with { SourceSha256 = new string('A', 64) };
        var results = await Task.WhenAll(new[] { preparation, changed }.Select(candidate =>
            Record.ExceptionAsync(async () => Assert.True(await Store().StoreAsync(claim, candidate, Token))).AsTask()));
        Assert.Single(results, exception => exception is null);
        var conflict = Assert.IsType<ArgumentException>(Assert.Single(results.OfType<Exception>()));
        Assert.Equal(nameof(preparation), conflict.ParamName);
        Assert.StartsWith("A prepared source identity", conflict.Message, StringComparison.Ordinal);
        await AssertCountsAsync(1, preparation.Chunks.Count);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(results[0] is null ? preparation.SourceSha256 : changed.SourceSha256,
            (await db.DocumentPreparationGenerations.SingleAsync(Token)).SourceSha256);
    }

    [Fact]
    public async Task DatabaseClockOwnsPreparedTimestamp()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        await using var db = _database.DbContextFactory.CreateDbContext();
        var before = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        Assert.True(await Store(clock: new SkewedClock(before.AddYears(1))).StoreAsync(claim, preparation, Token));
        var after = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        Assert.InRange((await db.DocumentPreparationGenerations.SingleAsync(Token)).PreparedAtUtc, before, after);
    }

    private DocumentIngestionInboxService Inbox(SaveChangesInterceptor? checkpoint = null) =>
        new(TimeProvider.System, checkpoint is null ? _database.DbContextFactory : _database.CreateDbContextFactory(checkpoint));

    private DocumentIngestionJobService Jobs() => new(Options.Create(Config), TimeProvider.System, _database.DbContextFactory);

    private DocumentPreparationStoreService Store(SaveChangesInterceptor? checkpoint = null, TimeProvider? clock = null) =>
        new(clock ?? TimeProvider.System, checkpoint is null ? _database.DbContextFactory : _database.CreateDbContextFactory(checkpoint));

    private async Task<DocumentIngestionClaim> ClaimAsync()
    {
        await Inbox().AcceptAsync(DocumentIngestionTestData.Notification(), Token);
        return Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(Token));
    }

    private async Task<PdfPreparationResult> PrepareAsync(DocumentChunkingConfig? settings = null)
    {
        using var stream = new MemoryStream(_bytes);
        return await new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), new DocumentChunkingService())
            .PrepareAsync(stream, settings ?? new(), Token);
    }

    private async Task AssertCountsAsync(int generations, int chunks)
    {
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(generations, await db.DocumentPreparationGenerations.CountAsync(Token));
        Assert.Equal(chunks, await db.DocumentTextChunks.CountAsync(Token));
    }

    private Task WaitForBlockedAdvisoryLockAsync() => _database.WaitForBlockedAdvisoryLockAsync(Token);

    private sealed class SaveCheckpoint(Func<AgentRuntimeDbContext, CancellationToken, Task> saved) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            await saved(Assert.IsType<AgentRuntimeDbContext>(eventData.Context), cancellationToken);
            return result;
        }
    }

    private sealed class SkewedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
#endif
