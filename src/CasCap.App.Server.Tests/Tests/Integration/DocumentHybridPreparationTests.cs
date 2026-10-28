#if DEBUG
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Provider = CasCap.Tests.DocumentHybridPreparationTestData.Provider;

namespace CasCap.Tests.Integration;

/// <summary>Exercises admission, actual source/PDF processing and durable recovery with isolated SQLite and fake paid providers.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "HybridPreparation")]
public sealed class DocumentHybridPreparationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly Clock _clock = new();
    private readonly Provider _provider = new();
    private readonly byte[] _bytes = DocumentPreparationTestData.Pdf("Synthetic page.");
    private Factory _factory = null!;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with { ProcessingEnabled = true };
    private static DocumentPreparationPlan Plan => DocumentHybridPreparationTestData.Plan;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        _factory = new(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).Options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidatesStayReviewOnlyAndReplayReusesReceipts(bool blank)
    {
        var claim = await ClaimAsync();
        var bytes = DocumentPreparationTestData.Pdf(blank ? "" : "Synthetic page.");
        var processor = Processor(bytes);
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        var operations = await Store().ReadAsync(claim, TestContext.Current.CancellationToken);
        Assert.Equal(2, operations.Count);
        Assert.All(operations, operation =>
        {
            Assert.Equal(DocumentPreparationOperations.Validated, operation.Status);
            Assert.Equal(DocumentPreparationIdentity.Hash(bytes), operation.SourceSha256);
            Assert.Equal(DocumentPreparationIdentity.Hash(claim.PreparationPlanJson!), operation.PlanSha256);
            Assert.NotNull(operation.UsageJson);
        });
        Assert.True(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReviewRequired, TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(DocumentIngestionStates.Queued, (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.AgentRuns.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordedCandidatesProjectToHeadingChunksWithoutReviewMetadata()
    {
        var claim = await ClaimAsync();
        _provider.Text = """
            {"page":1,"markdown":"# Control\n\n## Heat\n\nSource passage.","changes":["review change"],"uncertainties":["review uncertainty"]}
            """;
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired,
            await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        var operation = Assert.Single(await Store().ReadAsync(claim, TestContext.Current.CancellationToken),
            item => item.Stage == DocumentPreparationOperations.PreparationAgent);
        var receipt = JsonSerializer.Deserialize<DocumentPreparationAgentReceipt>(operation.ResultJson!, JsonSerializerOptions.Web)!;
        var candidate = DocumentPreparationCandidateValidator.Validate(receipt.Response, Plan, 1, 10_000);
        var projection = new DocumentChunkingService().ChunkMarkdown(
            [new(candidate.Page, candidate.Markdown)], new(), new(), TestContext.Current.CancellationToken);
        var chunk = Assert.Single(projection.Chunks);
        Assert.Equal("# Control\n## Heat\n\nSource passage.", chunk.EmbeddingText);
        Assert.DoesNotContain("review change", chunk.EmbeddingText, StringComparison.Ordinal);
        Assert.DoesNotContain("review uncertainty", chunk.EmbeddingText, StringComparison.Ordinal);
        var preview = DocumentPreparationReviewRenderer.RenderChunks(projection);
        Assert.Contains("Review-only markdown-headings chunks", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("review uncertainty", preview, StringComparison.Ordinal);
        Assert.Equal(["review change"], candidate.Changes);
        Assert.Equal(["review uncertainty"], candidate.Uncertainties);
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Fact]
    public async Task UnboundExistingJobRetainsLocalProcessing()
    {
        var claim = await ClaimAsync(paid: false);
        var bytes = DocumentPreparationTestData.Pdf("Synthetic local page.");
        var download = DocumentHybridPreparationTestData.Downloader(() => new MemoryStream(bytes), Config);
        var local = new PdfDocumentProcessor(download,
            new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), new DocumentChunkingService()),
            new DocumentPreparationStoreService(_clock, _factory));
        var router = new DocumentPreparationProcessor(local, Processor(bytes));
        Assert.Equal(DocumentProcessingOutcome.Completed, await router.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(0, _provider.Submissions + _provider.ModelCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().ReserveAsync(claim, Identity(claim), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("managed")]
    [InlineData("agent")]
    public async Task UnknownSubmissionNeverAutomaticallyReplays(string stage)
    {
        var claim = await ClaimAsync();
        _provider.FailSubmission = stage == "managed";
        _provider.FailModel = stage == "agent";
        var processor = Processor();
        Assert.Equal(DocumentProcessingOutcome.ReconciliationRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.ReconciliationRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(stage == "agent" ? 1 : 0, _provider.ModelCalls);
        Assert.True(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReconciliationRequired, TestContext.Current.CancellationToken));
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredIntentBlocksButManagedReceiptResumesGetOnly(bool receipt)
    {
        var claim = await ClaimAsync();
        var reservation = Assert.IsType<DocumentPreparationReservation>(
            await Store().ReserveAsync(claim, Identity(claim), TestContext.Current.CancellationToken));
        if (receipt)
            await Store().RecordOperationAsync(reservation.Operation, new Uri("https://layout.example.com/operation"));
        _clock.Advance(TimeSpan.FromSeconds(Config.ProcessingTimeoutSeconds));
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        await using (var db = _factory.CreateDbContext())
            Assert.Equal(receipt ? DocumentIngestionStates.RetryPending : DocumentIngestionStates.ReconciliationRequired,
                (await db.DocumentIngestionJobs.SingleAsync(TestContext.Current.CancellationToken)).Status);
        if (!receipt)
        {
            await Store().RecordOperationAsync(reservation.Operation, new Uri("https://layout.example.com/operation"));
            Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
            return;
        }
        _clock.Advance(TimeSpan.FromSeconds(DocumentProcessingPolicy.RetryDelaySeconds));
        var retry = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(retry, TestContext.Current.CancellationToken));
        Assert.Equal(0, _provider.Submissions);
        Assert.Equal(1, _provider.Polls);
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Fact]
    public async Task RecordedPageSurvivesExpiryWithoutAnotherCharge()
    {
        var claim = await ClaimAsync();
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(Config.ProcessingTimeoutSeconds));
        Assert.False(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.ReviewRequired, TestContext.Current.CancellationToken));
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(DocumentProcessingPolicy.RetryDelaySeconds));
        var retry = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(retry, TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{"page":2,"markdown":"Wrong page","changes":[],"uncertainties":[]}""")]
    [InlineData("""{"page":1,"markdown":"Text","changes":[],"uncertainties":[],"extra":true}""")]
    [InlineData("""{"page":1,"markdown":"Text","changes":null,"uncertainties":[]}""")]
    [InlineData("""{"page":1,"page":1,"markdown":"Text","changes":[],"uncertainties":[]}""")]
    public async Task InvalidResponseRetainsUsageAndCannotReinfer(string text)
    {
        var claim = await ClaimAsync();
        _provider.Text = text;
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        var operation = Assert.Single(await Store().ReadAsync(claim, TestContext.Current.CancellationToken),
            operation => operation.Stage == DocumentPreparationOperations.PreparationAgent);
        Assert.Equal(DocumentPreparationOperations.Rejected, operation.Status);
        Assert.Contains(text, JsonSerializer.Deserialize<DocumentPreparationAgentReceipt>(operation.ResultJson!, JsonSerializerOptions.Web)!.Response.Text);
        Assert.NotNull(operation.UsageJson);
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("incomplete")]
    [InlineData("tokens")]
    public async Task ProviderContractFailuresRetainKnownResponse(string invalid)
    {
        var claim = await ClaimAsync();
        _provider.ResponseChange = response => invalid switch
        {
            "model" => response with { ModelId = "wrong" },
            "incomplete" => response with { IsComplete = false },
            _ => response with { Usage = new UsageDetails { OutputTokenCount = 8193 } },
        };
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.ModelCalls);
        Assert.Equal(DocumentPreparationOperations.Rejected,
            (await Store().ReadAsync(claim, TestContext.Current.CancellationToken)).Single(operation => operation.PageNumber == 1).Status);
    }

    [Fact]
    public async Task MissingUsageRemainsUnknown()
    {
        var claim = await ClaimAsync();
        _provider.ResponseChange = response => response with { Usage = null };
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Null((await Store().ReadAsync(claim, TestContext.Current.CancellationToken))
            .Single(operation => operation.Stage == DocumentPreparationOperations.PreparationAgent).UsageJson);
    }

    [Fact]
    public async Task EveryAuthorizedPhysicalPageHasItsOwnReceipt()
    {
        var claim = await ClaimAsync();
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor(DocumentPreparationTestData.Pdf("One", "Two"))
            .ProcessAsync(claim, TestContext.Current.CancellationToken));
        var pages = (await Store().ReadAsync(claim, TestContext.Current.CancellationToken))
            .Where(operation => operation.Stage == DocumentPreparationOperations.PreparationAgent).ToArray();
        Assert.Equal([1, 2], pages.Select(operation => operation.PageNumber));
        Assert.All(pages, operation => Assert.NotNull(operation.DerivedPdfSha256));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(2, _provider.ModelCalls);
    }

    [Fact]
    public async Task SelectedPhysicalPagesKeepOriginalIdentity()
    {
        var claim = await ClaimAsync(settings: Plan.Settings with { PhysicalPages = [4, 2] });
        var bytes = DocumentPreparationTestData.Pdf("One", "Two", "Three", "Four");
        var processor = Processor(bytes);
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await processor.ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal([2, 4], Assert.IsType<int[]>(_provider.SubmittedPages));
        Assert.Equal(DocumentPreparationIdentity.Hash(bytes), _provider.SubmittedSourceSha256);
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(2, _provider.ModelCalls);
        var operations = await Store().ReadAsync(claim, TestContext.Current.CancellationToken);
        Assert.Equal([2, 4], operations.Where(operation => operation.Stage == DocumentPreparationOperations.PreparationAgent)
            .Select(operation => operation.PageNumber).Order());
        Assert.All(operations, operation => Assert.Equal(DocumentPreparationIdentity.Hash(bytes), operation.SourceSha256));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedPagesRespectSourceLimits(bool resourceLimit)
    {
        var claim = await ClaimAsync(settings: Plan.Settings with { PhysicalPages = resourceLimit ? [2, 4] : [2, 5] });
        var bytes = DocumentPreparationTestData.Pdf("One", "Two", "Three", "Four");
        var limits = new DocumentPreparationConfig { MaximumPages = resourceLimit ? 3 : 200 };
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure,
            await Processor(bytes, limits).ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(0, _provider.Submissions + _provider.ModelCalls);
        Assert.Empty(await Store().ReadAsync(claim, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResponseReceiptSurvivesCancellationBeforeItsObserver()
    {
        var claim = await ClaimAsync();
        using var cancellation = new CancellationTokenSource();
        _provider.BeforeResponse = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessAsync(claim, cancellation.Token));
        var operation = (await Store().ReadAsync(claim, TestContext.Current.CancellationToken))
            .Single(operation => operation.PageNumber == 1);
        Assert.Equal(DocumentPreparationOperations.Received, operation.Status);
        Assert.NotNull(operation.UsageJson);
        _provider.BeforeResponse = null;
        Assert.Equal(DocumentProcessingOutcome.ReviewRequired, await Processor().ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("etag")]
    [InlineData("plan")]
    public async Task ForgedClaimCannotAuthorizePaidWork(string changed)
    {
        var claim = await ClaimAsync();
        var forged = changed switch
        {
            "tenant" => claim with { TenantId = "tenant-b" },
            "etag" => claim with { ETag = "different" },
            _ => claim with { PreparationPlanJson = claim.PreparationPlanJson!.Replace("synthetic-revision", "wrong", StringComparison.Ordinal) },
        };
        Assert.Null(await Store().ReserveAsync(forged, Identity(forged), TestContext.Current.CancellationToken));
        Assert.Equal(0, _provider.Submissions + _provider.ModelCalls);
    }

    [Fact]
    public async Task IdentityAndReceiptAreWriteOnceAndTenantForeignKeyIsEnforced()
    {
        var claim = await ClaimAsync();
        var original = Assert.IsType<DocumentPreparationReservation>(
            await Store().ReserveAsync(claim, Identity(claim), TestContext.Current.CancellationToken));
        Assert.True(original.IsNew);
        Assert.False((await Store().ReserveAsync(claim, Identity(claim), TestContext.Current.CancellationToken))!.IsNew);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().ReserveAsync(claim,
            Identity(claim) with { RequestSha256 = new string('A', 64) }, TestContext.Current.CancellationToken));
        await Store().RecordResponseAsync(original.Operation, """{"status":"failed"}""", null);
        await Store().RecordResponseAsync(original.Operation, """{"status":"failed"}""", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store().RecordResponseAsync(original.Operation, """{"status":"different"}""", null));
        await using var db = _factory.CreateDbContext();
        var forged = await db.DocumentPreparationOperations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        forged.Id = new string('B', 64);
        forged.TenantId = "tenant-b";
        db.DocumentPreparationOperations.Add(forged);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AdmissionPinsVersionAndDuplicateCannotRetarget()
    {
        var claim = await ClaimAsync();
        await using (var db = _factory.CreateDbContext())
        {
            db.ActiveAgentDefinitions.Remove(await db.ActiveAgentDefinitions.SingleAsync(TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var notification = DocumentIngestionTestData.Notification() with { AgentPreparation = Plan.Settings with { DefinitionVersion = "later" } };
        await Inbox().AcceptAsync(notification, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Inbox().AcceptAsync(
            notification with { Event = DocumentIngestionTestData.Notification("event-2", "2").Event }, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentPreparationAgentTemplate.Version,
            JsonSerializer.Deserialize<DocumentPreparationPlan>(claim.PreparationPlanJson!, JsonSerializerOptions.Web)!.Definition.Version);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("pages")]
    [InlineData("bytes")]
    public async Task PreflightRejectsBeforeAnyPaidIntent(string invalid)
    {
        var claim = await ClaimAsync();
        _provider.FailVerification = invalid == "revision";
        var bytes = DocumentPreparationTestData.Pdf("One", "Two", "Three");
        var limits = invalid == "bytes" ? new DocumentPreparationConfig { MaximumPdfBytes = 20 } : new();
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure,
            await Processor(invalid == "pages" ? bytes : null, limits).ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(0, _provider.Submissions + _provider.ModelCalls);
        Assert.Empty(await Store().ReadAsync(claim, TestContext.Current.CancellationToken));
    }

    private async Task<DocumentIngestionClaim> ClaimAsync(bool paid = true, DocumentAgentPreparationConfig? settings = null)
    {
        if (paid)
            await DocumentHybridPreparationTestData.PublishAsync(_factory, _clock, TestContext.Current.CancellationToken);
        await Inbox().AcceptAsync(DocumentIngestionTestData.Notification() with { AgentPreparation = paid ? settings ?? Plan.Settings : null },
            TestContext.Current.CancellationToken);
        return Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
    }

    private HybridDocumentProcessor Processor(byte[]? bytes = null, DocumentPreparationConfig? limits = null) =>
        new(NullLogger<HybridDocumentProcessor>.Instance, Options.Create(limits ?? new()), _clock,
            DocumentHybridPreparationTestData.Downloader(() => new MemoryStream(bytes ?? _bytes), Config),
            new PdfDocumentPreparationService(Options.Create(limits ?? new()), new DocumentChunkingService()), Store(), _provider);

    private DocumentPreparationRequestIdentity Identity(DocumentIngestionClaim claim) =>
        DocumentHybridPreparationTestData.LayoutIdentity(claim, _bytes);

    private DocumentPreparationOperationStore Store() => new(_clock, _factory);
    private DocumentIngestionInboxService Inbox() => new(_clock, _factory);
    private DocumentIngestionJobService Jobs() => new(Options.Create(Config), _clock, _factory);

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }

}
#endif
