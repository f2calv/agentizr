#if DEBUG
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Verifies review-only generations on isolated PostgreSQL with synthetic layout/model receipts.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "PreparedDocuments")]
public sealed class PostgresDocumentMarkdownGenerationTests : IAsyncLifetime
{
    private const string BaselineMigration = "20261027125842_DocumentHybridPreparationOperations";
    private const string ReviewMigration = "20261028120307_DocumentMarkdownReviewGenerations";
    private readonly PostgreSqlFixture _database = new();
    private readonly DocumentHybridPreparationTestData.Provider _provider = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _database.InitializeAsync();
    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task JsonbRoundTripRetainsProvenanceAndReviewOnlyState()
    {
        var source = await SourceAsync();
        await using var db = _database.DbContextFactory.CreateDbContext();
        var before = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        var store = new DocumentPreparationMarkdownStore(Options.Create(new DocumentPreparationConfig()),
            new SkewedClock(before.AddYears(1)), _database.DbContextFactory, new());
        var id = Assert.IsType<string>(await store.StoreAsync(source, new(), Review(), Token));
        Assert.Equal(id, await store.StoreAsync(source, new(), Review(), Token));
        var snapshot = Assert.IsType<DocumentMarkdownReviewGeneration>(await store.ReadAsync(source, id, Token));
        var projection = new DocumentChunkingService().ChunkMarkdown(
            [new(1, DocumentHybridPreparationTestData.ReviewMarkdown)], new(), new(), Review(), Token);
        Assert.Equal(JsonSerializer.Serialize(projection, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(snapshot.Projection, JsonSerializerOptions.Web));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, snapshot.State);
        Assert.Equal(["change-note"], Assert.Single(snapshot.Candidates).Changes);
        Assert.Equal(["uncertainty-note"], snapshot.Candidates[0].Uncertainties);
        Assert.DoesNotContain("uncertainty-note", snapshot.Projection.Chunks[0].EmbeddingText, StringComparison.Ordinal);
        var row = await db.DocumentMarkdownGenerations.SingleAsync(Token);
        var after = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        Assert.InRange(row.CreatedAtUtc, before, after);
        var receipts = await db.DocumentPreparationOperations.AsNoTracking().ToArrayAsync(Token);
        Assert.Contains(receipts, receipt => receipt.ResultSha256 != DocumentPreparationIdentity.Hash(receipt.ResultJson!));
        Assert.Equal(receipts.Select(receipt => receipt.ResultSha256).Order(),
            snapshot.Receipts.Select(receipt => receipt.ResultSha256).Order());
        var type = await db.Database.SqlQuery<string>($"""
            SELECT udt_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'agent_runtime' AND table_name = 'document_markdown_generations' AND column_name = 'payload_json'
            """).SingleAsync(Token);
        Assert.Equal("jsonb", type);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(DocumentIngestionStates.Queued, (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Equal(1, _provider.ModelCalls);
        Assert.Equal(1, _provider.Submissions);
    }

    [Fact]
    public async Task ConcurrentRetriesCommitOneImmutableGeneration()
    {
        var source = await SourceAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Store().StoreAsync(source, new(), Review(), Token)));
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Single(results.Distinct());
        await AssertCountAsync(1);
        Assert.NotNull(await Store().ReadAsync(source, results[0]!, Token));
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonbReceiptBoundsRejectBeforeParsing(bool missing)
    {
        var source = await SourceAsync();
        await using (var db = _database.DbContextFactory.CreateDbContext())
        {
            var row = await db.DocumentPreparationOperations.SingleAsync(
                item => item.Stage == DocumentPreparationOperations.PreparationAgent, Token);
            row.ResultJson = missing ? null : JsonSerializer.Serialize(new { padding = new string('x', 4_097) });
            await db.SaveChangesAsync(Token);
        }
        var store = new DocumentPreparationMarkdownStore(Options.Create(new DocumentPreparationConfig { MaximumExtractedCharacters = 512 }),
            TimeProvider.System, _database.DbContextFactory, new());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.StoreAsync(source, new(), Review(), Token));
        Assert.Contains("bounded preparation receipts", error.Message, StringComparison.Ordinal);
        await AssertCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReviewAndProfileChangesAppend(bool profile)
    {
        var source = await SourceAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Store().StoreAsync(source,
            profile && index % 2 == 1 ? new() { TargetTokensPerChunk = 128 } : new(),
            !profile && index % 2 == 1 ? Review() with { ReviewId = "second-review" } : Review(), Token)));
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(2, results.Distinct().Count());
        await AssertCountAsync(2);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    [InlineData("sequence")]
    [InlineData("etag")]
    [InlineData("blob")]
    [InlineData("plan")]
    public async Task ExactSourceFencesWritesAndReads(string field)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        var changed = field switch
        {
            "tenant" => source with { TenantId = "tenant-b" },
            "collection" => source with { CollectionId = "other" },
            "document" => source with { DocumentId = new string('B', 64) },
            "job" => source with { JobId = new string('B', 64) },
            "sequence" => source with { Sequencer = "2".PadLeft(128, '0') },
            "etag" => source with { ETag = "different-etag" },
            "blob" => source with { BlobUri = "https://storage.example.com/documents/other.pdf" },
            "plan" => source with { PreparationPlanJson = "{}" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.Null(await Store().StoreAsync(changed, new(), Review(), Token));
        Assert.Null(await Store().ReadAsync(changed, id, Token));
        await AssertCountAsync(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AdmissionAndReviewGenerationSerialize(bool deleted, bool admissionFirst)
    {
        var source = await SourceAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new SaveCheckpoint(async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
        });
        var next = DocumentIngestionTestData.Notification("next-event", "2", deleted);
        Task<string?>? storing = null;
        Task? admitting = null;
        try
        {
            if (admissionFirst)
                admitting = Inbox(checkpoint).AcceptAsync(next, Token);
            else
                storing = Store(checkpoint).StoreAsync(source, new(), Review(), Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            if (admissionFirst)
                storing = Store().StoreAsync(source, new(), Review(), Token);
            else
                admitting = Inbox().AcceptAsync(next, Token);
            await WaitForBlockedAdvisoryLockAsync();
            Assert.NotNull(storing);
            Assert.NotNull(admitting);
            Assert.False(storing.IsCompleted);
            Assert.False(admitting.IsCompleted);
            await AssertCountAsync(0);
            release.SetResult();
            await admitting;
            if (admissionFirst)
                Assert.Null(await storing);
            else
                Assert.NotNull(await storing);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(storing ?? Task.CompletedTask, admitting ?? Task.CompletedTask);
        }
        Assert.Null(await Store().StoreAsync(source, new(), Review(), Token));
        if (await storing is { } id)
            Assert.Null(await Store().ReadAsync(source, id, Token));
        await AssertCountAsync(admissionFirst ? 0 : 1);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancellation")]
    [InlineData("source")]
    public async Task RollbackAfterInsert(string reason)
    {
        var source = await SourceAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var checkpoint = new SaveCheckpoint(async (db, token) =>
        {
            Assert.Equal(1, await db.DocumentMarkdownGenerations.CountAsync(token));
            await AssertCountAsync(0);
            if (reason == "failure")
                throw new IOException("Synthetic post-insert failure.");
            if (reason == "cancellation")
                await cancellation.CancelAsync();
            if (reason == "source")
                await db.KnowledgeDocuments.ExecuteUpdateAsync(update => update.SetProperty(item => item.Sequencer, "2"), token);
        });
        if (reason == "failure")
            await Assert.ThrowsAsync<IOException>(() => Store(checkpoint).StoreAsync(source, new(), Review(), cancellation.Token));
        else if (reason == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(checkpoint).StoreAsync(source, new(), Review(), cancellation.Token));
        else
            Assert.Null(await Store(checkpoint).StoreAsync(source, new(), Review(), cancellation.Token));
        await AssertCountAsync(0);
        Assert.NotNull(await Store().StoreAsync(source, new(), Review(), Token));
    }

    [Fact]
    public async Task AdvisoryLockCancellationLeavesNoGeneration()
    {
        var source = await SourceAsync();
        await using var owner = _database.DbContextFactory.CreateDbContext();
        await using var held = await owner.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(owner, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = Store().StoreAsync(source, new(), Review(), cancellation.Token);
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
        await AssertCountAsync(0);
        Assert.NotNull(await Store().StoreAsync(source, new(), Review(), Token));
    }

    [Fact]
    public async Task ReceiptChangeBeforeLockedRecheckRejects()
    {
        var source = await SourceAsync();
        await using var owner = _database.DbContextFactory.CreateDbContext();
        await using var held = await owner.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(owner, Token);
        var pending = Store().StoreAsync(source, new(), Review(), Token);
        try
        {
            await WaitForBlockedAdvisoryLockAsync();
            await owner.DocumentPreparationOperations.Where(item => item.Stage == DocumentPreparationOperations.PreparationAgent)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.ResultJson, "{}"), Token);
        }
        finally
        {
            await held.CommitAsync(Token);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        await AssertCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeOwnershipConstraint(bool tenant)
    {
        var source = await SourceAsync();
        await Store().StoreAsync(source, new(), Review(), Token);
        await using var db = _database.DbContextFactory.CreateDbContext();
        var row = await db.DocumentMarkdownGenerations.SingleAsync(Token);
        if (tenant)
            row.TenantId = "tenant-b";
        else
            row.CollectionId = "other";
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task UpgradePreservesExistingPaidReceiptsAndAllowsReviewStorage()
    {
        await using var previous = new PostgreSqlFixture { InitialMigration = BaselineMigration };
        await previous.InitializeAsync();
        var source = await DocumentHybridPreparationTestData.PrepareReviewAsync(previous.DbContextFactory, _provider, Token);
        await using var db = previous.DbContextFactory.CreateDbContext();
        var before = await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token);
        Assert.DoesNotContain(ReviewMigration, await db.Database.GetAppliedMigrationsAsync(Token));
        await db.Database.MigrateAsync(Token);
        Assert.Contains(ReviewMigration, await db.Database.GetAppliedMigrationsAsync(Token));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(
            await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token)));
        var store = new DocumentPreparationMarkdownStore(Options.Create(new DocumentPreparationConfig()),
            TimeProvider.System, previous.DbContextFactory, new());
        var id = Assert.IsType<string>(await store.StoreAsync(source, new(), Review(), Token));
        Assert.NotNull(await store.ReadAsync(source, id, Token));
    }

    private Task<DocumentIngestionClaim> SourceAsync() =>
        DocumentHybridPreparationTestData.PrepareReviewAsync(_database.DbContextFactory, _provider, Token);

    private static DocumentMarkdownSourceReview Review() => DocumentHybridPreparationTestData.SourceReview();

    private DocumentPreparationMarkdownStore Store(SaveChangesInterceptor? checkpoint = null) =>
        new(Options.Create(new DocumentPreparationConfig()), TimeProvider.System,
            checkpoint is null ? _database.DbContextFactory : _database.CreateDbContextFactory(checkpoint), new());

    private DocumentIngestionInboxService Inbox(SaveChangesInterceptor? checkpoint = null) =>
        new(TimeProvider.System, checkpoint is null ? _database.DbContextFactory : _database.CreateDbContextFactory(checkpoint));

    private async Task AssertCountAsync(int expected)
    {
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(expected, await db.DocumentMarkdownGenerations.CountAsync(Token));
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
