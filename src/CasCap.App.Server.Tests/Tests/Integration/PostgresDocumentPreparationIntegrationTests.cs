#if DEBUG
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Validates migrated PostgreSQL fencing, JSONB receipts and recovery with synthetic paid-provider boundaries.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "PreparationProviders")]
public sealed class PostgresDocumentPreparationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _database = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with { ProcessingEnabled = true };
    private static DocumentPreparationPlan Plan => DocumentHybridPreparationTestData.Plan;

    public ValueTask InitializeAsync() => _database.InitializeAsync();
    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ConcurrentClaims()
    {
        await AdmitAsync();
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Jobs().ClaimAsync(Token)));
        var owner = Assert.Single(claims.OfType<DocumentIngestionClaim>());
        await using var db = _database.DbContextFactory.CreateDbContext();
        var job = await db.DocumentIngestionJobs.SingleAsync(Token);
        Assert.Equal(owner.ClaimId, job.ClaimId);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public async Task ConcurrentReservations()
    {
        var claim = await ClaimAsync();
        var identity = Identity(claim);
        var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Store().ReserveAsync(claim, identity, Token)));
        Assert.All(reservations, reservation => Assert.NotNull(reservation));
        Assert.Single(reservations, reservation => reservation!.IsNew);
        Assert.Single(reservations.Select(reservation => reservation!.Operation.Id).Distinct());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store().ReserveAsync(claim, identity with { RequestSha256 = new string('A', 64) }, Token));
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await db.DocumentPreparationOperations.CountAsync(Token));
    }

    [Fact]
    public async Task AdvisoryLockCancellation()
    {
        var claim = await ClaimAsync();
        await using var owner = _database.DbContextFactory.CreateDbContext();
        await using var held = await owner.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(owner, Token);
        await using var waiter = _database.DbContextFactory.CreateDbContext();
        await waiter.Database.OpenConnectionAsync(Token);
        var processId = ((NpgsqlConnection)waiter.Database.GetDbConnection()).ProcessID;
        await using var waiting = await waiter.Database.BeginTransactionAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var pending = DocumentIngestionTransactions.LockAsync(waiter, cancellation.Token);
        try
        {
            while (!await owner.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid = {processId}
                    AND locktype = 'advisory' AND NOT granted) AS "Value"
                """).SingleAsync(deadline.Token))
                await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
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
            await waiting.RollbackAsync(Token);
            await held.RollbackAsync(Token);
        }
        Assert.True(Assert.IsType<DocumentPreparationReservation>(
            await Store().ReserveAsync(claim, Identity(claim), Token)).IsNew);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("claim")]
    [InlineData("source")]
    [InlineData("plan")]
    public async Task ClaimFences(string changed)
    {
        var claim = await ClaimAsync();
        var stale = changed switch
        {
            "tenant" => claim with { TenantId = "tenant-b" },
            "collection" => claim with { CollectionId = "other" },
            "document" => claim with { DocumentId = new string('B', 64) },
            "claim" => claim with { ClaimId = Guid.NewGuid() },
            "source" => claim with { ETag = "different-etag" },
            "plan" => claim with { PreparationPlanJson = "{}" },
            _ => throw new ArgumentOutOfRangeException(nameof(changed)),
        };
        Assert.Null(await Store().ReserveAsync(stale, Identity(stale), Token));
        Assert.False(await Jobs().FinishAsync(stale, DocumentProcessingOutcome.ReviewRequired, Token));
        Assert.Empty(await Store().ReadAsync(stale, Token));
        Assert.True(Assert.IsType<DocumentPreparationReservation>(
            await Store().ReserveAsync(claim, Identity(claim), Token)).IsNew);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteOnceReceipts(bool response)
    {
        var claim = await ClaimAsync();
        var identity = Identity(claim);
        var first = Assert.IsType<DocumentPreparationReservation>(await Store().ReserveAsync(claim, identity, Token));
        var second = Assert.IsType<DocumentPreparationReservation>(await Store().ReserveAsync(claim, identity, Token));
        async Task<bool> WriteAsync(DocumentPreparationOperationEntity operation, int value)
        {
            try
            {
                if (response)
                    await Store().RecordResponseAsync(operation, $$"""{"value":{{value}}}""", """{"tokens":15}""");
                else
                    await Store().RecordOperationAsync(operation, new Uri($"https://layout.example.com/operations/{value}"));
                return true;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("cannot be replaced.", StringComparison.Ordinal))
            {
                return false;
            }
        }
        var writes = await Task.WhenAll(WriteAsync(first.Operation, 1), WriteAsync(second.Operation, 2));
        Assert.Single(writes, written => written);
        var saved = Assert.Single(await Store().ReadAsync(claim, Token));
        if (response)
        {
            using var result = JsonDocument.Parse(saved.ResultJson!);
            Assert.Equal(writes[0] ? 1 : 2, result.RootElement.GetProperty("value").GetInt32());
            using var usage = JsonDocument.Parse(saved.UsageJson!);
            Assert.Equal(15, usage.RootElement.GetProperty("tokens").GetInt32());
        }
        else
            Assert.Equal($"https://layout.example.com/operations/{(writes[0] ? 1 : 2)}", saved.OperationUri);
    }

    [Theory]
    [InlineData(DocumentPreparationOperations.Submitting)]
    [InlineData(DocumentPreparationOperations.Polling)]
    [InlineData(DocumentPreparationOperations.Received)]
    public async Task ExpiredClaimRecovery(string stage)
    {
        var claim = await ClaimAsync();
        var intent = Assert.IsType<DocumentPreparationReservation>(await Store().ReserveAsync(claim, Identity(claim), Token));
        if (stage == DocumentPreparationOperations.Polling)
            await Store().RecordOperationAsync(intent.Operation, new Uri("https://layout.example.com/operation"));
        if (stage == DocumentPreparationOperations.Received)
            await Store().RecordResponseAsync(intent.Operation, """{"status":"Succeeded"}""", null);
        await using var db = _database.DbContextFactory.CreateDbContext();
        var now = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        await db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.ClaimExpiresAtUtc, now.AddSeconds(-1)), Token);
        Assert.Null(await Store().ReserveAsync(claim, Identity(claim), Token));
        Assert.False(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReviewRequired, Token));
        Assert.Null(await Jobs().ClaimAsync(Token));
        var recovered = await db.DocumentIngestionJobs.AsNoTracking().SingleAsync(Token);
        if (stage == DocumentPreparationOperations.Submitting)
        {
            Assert.Equal(DocumentIngestionStates.ReconciliationRequired, recovered.Status);
            await Store().RecordOperationAsync(intent.Operation, new Uri("https://layout.example.com/late-operation"));
            await Store().RecordResponseAsync(intent.Operation, """{"status":"Succeeded"}""", null);
            Assert.Null(await Jobs().ClaimAsync(Token));
            Assert.Equal(DocumentIngestionStates.ReconciliationRequired,
                (await db.DocumentIngestionJobs.AsNoTracking().SingleAsync(Token)).Status);
        }
        else
        {
            Assert.Equal(DocumentIngestionStates.RetryPending, recovered.Status);
            Assert.True(recovered.NextAttemptAtUtc >= now.AddSeconds(30));
            await db.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAtUtc, now.AddSeconds(-1)), Token);
            var replacement = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(Token));
            Assert.NotEqual(claim.ClaimId, replacement.ClaimId);
            Assert.Equal(claim.PreparationPlanJson, replacement.PreparationPlanJson);
            var resumed = Assert.IsType<DocumentPreparationReservation>(
                await Store().ReserveAsync(replacement, Identity(replacement), Token));
            Assert.False(resumed.IsNew);
            Assert.Equal(intent.Operation.Id, resumed.Operation.Id);
            Assert.Equal(claim.ClaimId, resumed.Operation.SubmissionClaimId);
        }
    }

    [Theory]
    [InlineData(null, 1, 1)]
    [InlineData(DocumentPreparationOperations.Polling, 0, 1)]
    [InlineData(DocumentPreparationOperations.Received, 0, 0)]
    public async Task JsonbHybridReplayRemainsReviewOnly(string? receipt, int submissions, int polls)
    {
        var claim = await ClaimAsync();
        var bytes = DocumentPreparationTestData.Pdf("Synthetic page.");
        if (receipt is not null)
        {
            var intent = Assert.IsType<DocumentPreparationReservation>(await Store().ReserveAsync(
                claim, DocumentHybridPreparationTestData.LayoutIdentity(claim, bytes), Token));
            await Store().RecordOperationAsync(intent.Operation, new Uri("https://layout.example.com/operation"));
            if (receipt == DocumentPreparationOperations.Received)
                await Store().RecordResponseAsync(intent.Operation, JsonSerializer.Serialize(new DocumentLayoutPollResult
                {
                    Status = "Succeeded",
                    Markdown = "Managed draft",
                    Pages = new Dictionary<int, string> { [1] = "Managed draft" },
                }, JsonSerializerOptions.Web), null);
            await using var recovery = _database.DbContextFactory.CreateDbContext();
            var now = await DocumentIngestionTransactions.GetUtcNowAsync(recovery, TimeProvider.System, Token);
            await recovery.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.ClaimExpiresAtUtc, now.AddSeconds(-1)), Token);
            Assert.Null(await Jobs().ClaimAsync(Token));
            await recovery.DocumentIngestionJobs.Where(job => job.Id == claim.JobId)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.NextAttemptAtUtc, now.AddSeconds(-1)), Token);
            var replacement = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(Token));
            Assert.NotEqual(claim.ClaimId, replacement.ClaimId);
            claim = replacement;
        }
        var provider = new DocumentHybridPreparationTestData.Provider();
        HybridDocumentProcessor Processor() => new(NullLogger<HybridDocumentProcessor>.Instance, Options.Create(new DocumentPreparationConfig()),
            TimeProvider.System, DocumentHybridPreparationTestData.Downloader(() => new MemoryStream(bytes), Config),
            new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), new DocumentChunkingService()), Store(), provider);
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(claim, Token));
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(claim, Token));
        Assert.Equal(submissions, provider.Submissions);
        Assert.Equal(polls, provider.Polls);
        Assert.Equal(1, provider.ModelCalls);
        var operations = await Store().ReadAsync(claim, Token);
        Assert.Equal(2, operations.Count);
        Assert.All(operations, operation => Assert.Equal(DocumentPreparationOperations.Validated, operation.Status));
        var model = Assert.Single(operations, operation => operation.Stage == DocumentPreparationOperations.PreparationAgent);
        using var usage = JsonDocument.Parse(model.UsageJson!);
        Assert.Equal(10, usage.RootElement.GetProperty("inputTokenCount").GetInt64());
        Assert.Equal(5, usage.RootElement.GetProperty("outputTokenCount").GetInt64());
        Assert.Equal(15, usage.RootElement.GetProperty("totalTokenCount").GetInt64());
        Assert.True(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReviewRequired, Token));
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        var types = await db.Database.SqlQuery<string>($"""
            SELECT udt_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'agent_runtime'
                AND ((table_name = 'document_ingestion_jobs' AND column_name = 'preparation_plan_json')
                    OR (table_name = 'document_preparation_operations' AND column_name IN ('result_json', 'usage_json')))
            """).ToArrayAsync(Token);
        Assert.Equal(3, types.Length);
        Assert.All(types, type => Assert.Equal("jsonb", type));
    }

    [Fact]
    public async Task DatabaseClockOwnsClaimDeadline()
    {
        await AdmitAsync();
        await using var db = _database.DbContextFactory.CreateDbContext();
        var before = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        var clock = new SkewedClock(before.AddYears(1));
        var claim = Assert.IsType<DocumentIngestionClaim>(await new DocumentIngestionJobService(
            Options.Create(Config), clock, _database.DbContextFactory).ClaimAsync(Token));
        var after = await DocumentIngestionTransactions.GetUtcNowAsync(db, TimeProvider.System, Token);
        Assert.InRange(claim.ExpiresAtUtc, before.AddSeconds(Config.ProcessingTimeoutSeconds),
            after.AddSeconds(Config.ProcessingTimeoutSeconds));
        Assert.NotNull(await new DocumentPreparationOperationStore(clock, _database.DbContextFactory)
            .ReserveAsync(claim, Identity(claim), Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceRevisionFencesLateReceipts(bool deleted)
    {
        var claim = await ClaimAsync();
        var intent = Assert.IsType<DocumentPreparationReservation>(await Store().ReserveAsync(claim, Identity(claim), Token));
        await new DocumentIngestionInboxService(TimeProvider.System, _database.DbContextFactory).AcceptAsync(
            DocumentIngestionTestData.Notification("next-event", "2", deleted) with { AgentPreparation = Plan.Settings }, Token);
        Assert.Null(await Store().ReserveAsync(claim, Identity(claim), Token));
        Assert.False(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReviewRequired, Token));
        await Store().RecordResponseAsync(intent.Operation, """{"status":"Succeeded"}""", null);
        var saved = Assert.Single(await Store().ReadAsync(claim, Token));
        Assert.Equal(DocumentPreparationOperations.Received, saved.Status);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.Ignored,
            (await db.DocumentIngestionJobs.SingleAsync(job => job.Id == claim.JobId, Token)).Status);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
    }

    [Fact]
    public async Task CompositeOwnershipConstraint()
    {
        var claim = await ClaimAsync();
        var operation = Assert.IsType<DocumentPreparationReservation>(
            await Store().ReserveAsync(claim, Identity(claim), Token)).Operation;
        await using var db = _database.DbContextFactory.CreateDbContext();
        db.Attach(operation);
        operation.TenantId = "tenant-b";
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    private async Task AdmitAsync()
    {
        await using var db = _database.DbContextFactory.CreateDbContext();
        db.ActiveAgentDefinitions.Add(new()
        {
            TenantId = Plan.TenantId,
            AgentName = Plan.Definition.Name,
            Snapshot = new AgentDefinitionSnapshotEntity
            {
                TenantId = Plan.TenantId,
                AgentName = Plan.Definition.Name,
                DefinitionVersion = Plan.Definition.Version,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(Plan.Definition, JsonSerializerOptions.Web),
                PublishedAtUtc = TimeProvider.System.GetUtcNow(),
            },
            UpdatedAtUtc = TimeProvider.System.GetUtcNow(),
        });
        await db.SaveChangesAsync(Token);
        await new DocumentIngestionInboxService(TimeProvider.System, _database.DbContextFactory)
            .AcceptAsync(DocumentIngestionTestData.Notification() with { AgentPreparation = Plan.Settings }, Token);
    }

    private async Task<DocumentIngestionClaim> ClaimAsync()
    {
        await AdmitAsync();
        return Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(Token));
    }

    private static DocumentPreparationRequestIdentity Identity(DocumentIngestionClaim claim) =>
        new(new string('1', 64), DocumentPreparationIdentity.Hash(claim.PreparationPlanJson!),
            new string('2', 64), DocumentPreparationOperations.DocumentIntelligence, 0);

    private DocumentPreparationOperationStore Store() => new(TimeProvider.System, _database.DbContextFactory);
    private DocumentIngestionJobService Jobs() => new(Options.Create(Config), TimeProvider.System, _database.DbContextFactory);

    private sealed class SkewedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
#endif
