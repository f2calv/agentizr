#if DEBUG
using Azure;
using CasCap.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Proves transactional preparation with SQLite and fake Blob streams, not PostgreSQL races or live storage.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PreparedDocuments")]
public sealed class DocumentPreparationPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly Clock _clock = new();
    private readonly SaveCheckpoint _checkpoint = new();
    private Factory _factory = null!;
    private readonly DocumentChunkingService _chunker = new();
    private readonly byte[] _bytes = DocumentPreparationTestData.Pdf("Synthetic reference.");

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        _factory = new(new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseSqlite(_connection).AddInterceptors(_checkpoint).Options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task RealProcessorStagesBeforeCompletingJob()
    {
        await AcceptAsync();
        var bytes = DocumentPreparationTestData.Pdf("Synthetic reference.", "", "Another source page.");
        var processor = Processor(() => new MemoryStream(bytes));
        var processing = new DocumentIngestionProcessingService(NullLogger<DocumentIngestionProcessingService>.Instance,
            Options.Create(Config), _clock, Jobs(), [processor]);
        Assert.True(await processing.ProcessOneAsync(TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        var generation = await db.DocumentPreparationGenerations.SingleAsync(TestContext.Current.CancellationToken);
        var chunks = await db.DocumentTextChunks.OrderBy(item => item.Ordinal).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DocumentIngestionStates.Completed,
            (await db.DocumentIngestionJobs.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(DocumentIngestionStates.Queued,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), generation.SourceSha256);
        Assert.Equal(3, generation.PageCount);
        Assert.Equal(2, Assert.Single(Assert.IsType<int[]>(JsonSerializer.Deserialize<int[]>(generation.PagesWithoutTextJson))));
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
    }

    [Fact]
    public async Task ExpiredClaimRetryDeduplicatesCommittedOutput()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        Assert.True(await Store().StoreAsync(claim, preparation, TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(Config.ProcessingTimeoutSeconds));
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(DocumentProcessingPolicy.RetryDelaySeconds));
        var retry = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(claim.ClaimId, retry.ClaimId);
        Assert.True(await Store().StoreAsync(retry, preparation, TestContext.Current.CancellationToken));
        Assert.True(await Jobs().FinishAsync(retry, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
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
    public async Task ExactClaimFencesWritesAndOutcomes(string changed)
    {
        var claim = await ClaimAsync();
        var forged = changed switch
        {
            "document" => claim with { DocumentId = "another-document" },
            "tenant" => claim with { TenantId = "another-tenant" },
            "collection" => claim with { CollectionId = "another-collection" },
            "sequencer" => claim with { Sequencer = "2".PadLeft(128, '0') },
            "blob" => claim with { BlobUri = "https://storage.example.com/documents/another.pdf" },
            "etag" => claim with { ETag = "another-etag" },
            "claim" => claim with { ClaimId = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(changed)),
        };
        Assert.False(await Store().StoreAsync(forged, await PrepareAsync(), TestContext.Current.CancellationToken));
        Assert.False(await Jobs().FinishAsync(forged, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersessionOrDeletionPreventsStaging(bool deleted)
    {
        var claim = await ClaimAsync();
        await Inbox().AcceptAsync(DocumentIngestionTestData.Notification("later", "2", deleted), TestContext.Current.CancellationToken);
        Assert.False(await Store().StoreAsync(claim, await PrepareAsync(), TestContext.Current.CancellationToken));
        Assert.False(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task ExpiryDuringSaveRollsBackAllOutput()
    {
        var claim = await ClaimAsync();
        _checkpoint.OnSaved = () => _clock.Advance(TimeSpan.FromSeconds(Config.ProcessingTimeoutSeconds));
        Assert.False(await Store().StoreAsync(claim, await PrepareAsync(), TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task FailureAfterInsertRollsBackAllOutput()
    {
        var claim = await ClaimAsync();
        _checkpoint.OnSaved = () => throw new IOException("Synthetic database write failure.");
        await Assert.ThrowsAsync<IOException>(() =>
            Processor(() => new MemoryStream(_bytes)).ProcessAsync(claim, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task TenantsDoNotSharePreparedGenerations()
    {
        await AcceptAsync();
        await Inbox().AcceptAsync(DocumentIngestionTestData.Notification("tenant-b-event", tenant: "tenant-b"),
            TestContext.Current.CancellationToken);
        var preparation = await PrepareAsync();
        for (var index = 0; index < 2; index++)
        {
            var claim = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
            Assert.True(await Store().StoreAsync(claim, preparation, TestContext.Current.CancellationToken));
            Assert.True(await Jobs().FinishAsync(claim, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        }
        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.DocumentPreparationGenerations.Select(item => item.TenantId).Distinct()
            .CountAsync(TestContext.Current.CancellationToken));
        await AssertCountsAsync(2, 2 * preparation.Chunks.Count);
    }

    [Fact]
    public async Task CancellationAfterInsertRollsBackAllOutput()
    {
        var claim = await ClaimAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _checkpoint.OnSaved = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Store().StoreAsync(claim, await PrepareAsync(), cancellation.Token));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task ProfilesStaySeparateAndOriginalIdentityIsImmutable()
    {
        var claim = await ClaimAsync();
        var preparation = await PrepareAsync();
        Assert.True(await Store().StoreAsync(claim, preparation, TestContext.Current.CancellationToken));
        var different = await PrepareAsync(new DocumentChunkingConfig { MaximumTokensPerChunk = 128, OverlapTokens = 16 });
        Assert.True(await Store().StoreAsync(claim, different, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Store().StoreAsync(claim,
            preparation with { SourceSha256 = new string('A', 64) }, TestContext.Current.CancellationToken));
        await AssertCountsAsync(2, preparation.Chunks.Count + different.Chunks.Count);
    }

    [Fact]
    public async Task TenantForeignKeyRejectsCrossCollectionChunk()
    {
        var claim = await ClaimAsync();
        Assert.True(await Store().StoreAsync(claim, await PrepareAsync(), TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        var chunk = await db.DocumentTextChunks.SingleAsync(TestContext.Current.CancellationToken);
        chunk.CollectionId = "unassigned";
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(19, Assert.IsType<SqliteException>(exception.InnerException).SqliteErrorCode);
    }

    [Theory]
    [InlineData(404, nameof(DocumentProcessingOutcome.PermanentFailure))]
    [InlineData(412, nameof(DocumentProcessingOutcome.PermanentFailure))]
    [InlineData(408, nameof(DocumentProcessingOutcome.RetryableFailure))]
    [InlineData(429, nameof(DocumentProcessingOutcome.RetryableFailure))]
    [InlineData(500, nameof(DocumentProcessingOutcome.RetryableFailure))]
    [InlineData(503, nameof(DocumentProcessingOutcome.RetryableFailure))]
    public async Task ExpectedSourceFailure(int status, string expected)
    {
        var claim = await ClaimAsync();
        Assert.Equal(expected, (await Processor(() => throw new RequestFailedException(status, "Synthetic source failure."))
            .ProcessAsync(claim, TestContext.Current.CancellationToken)).ToString());
        await AssertCountsAsync(0, 0);
    }

    [Theory]
    [InlineData(404, nameof(DocumentProcessingOutcome.PermanentFailure))]
    [InlineData(412, nameof(DocumentProcessingOutcome.PermanentFailure))]
    [InlineData(503, nameof(DocumentProcessingOutcome.RetryableFailure))]
    [InlineData(-1, nameof(DocumentProcessingOutcome.RetryableFailure))]
    public async Task FailureDuringReadDiscardsPartialSource(int status, string expected)
    {
        var claim = await ClaimAsync();
        var source = new InterruptedStream(_bytes, status);
        Assert.Equal(expected,
            (await Processor(() => source).ProcessAsync(claim, TestContext.Current.CancellationToken)).ToString());
        Assert.False(source.CanRead);
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task MissingETagFailsWithoutDownloading()
    {
        await AcceptAsync();
        await using (var db = _factory.CreateDbContext())
        {
            var document = await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken);
            document.ETag = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var claim = Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        var outcome = await Processor(() => throw new InvalidOperationException("No source should be opened."))
            .ProcessAsync(claim, TestContext.Current.CancellationToken);
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure, outcome);
        Assert.True(await Jobs().FinishAsync(claim, outcome, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    [InlineData(403)]
    public async Task UnexpectedSourceFailurePropagates(int status)
    {
        var claim = await ClaimAsync();
        var exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            Processor(() => throw new RequestFailedException(status, "Synthetic operator failure."))
                .ProcessAsync(claim, TestContext.Current.CancellationToken));
        Assert.Equal(status, exception.Status);
        await AssertCountsAsync(0, 0);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("oversized")]
    [InlineData("blank")]
    public async Task RejectedPreparationIsNotPersisted(string input)
    {
        var claim = await ClaimAsync();
        var bytes = input == "invalid" ? "Not a PDF"u8.ToArray()
            : DocumentPreparationTestData.Pdf(input == "blank" ? "" : "Synthetic source.");
        var config = input == "oversized" ? new DocumentPreparationConfig { MaximumPdfBytes = 10 } : new();
        Assert.Equal(DocumentProcessingOutcome.PermanentFailure,
            await Processor(() => new MemoryStream(bytes), config).ProcessAsync(claim, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task LostClaimIsNotCompleted()
    {
        var claim = await ClaimAsync();
        _clock.Advance(TimeSpan.FromSeconds(Config.ProcessingTimeoutSeconds));
        var bytes = DocumentPreparationTestData.Pdf("Synthetic source.");
        var outcome = await Processor(() => new MemoryStream(bytes)).ProcessAsync(claim, TestContext.Current.CancellationToken);
        Assert.Equal(DocumentProcessingOutcome.ClaimLost, outcome);
        Assert.False(await Jobs().FinishAsync(claim, outcome, TestContext.Current.CancellationToken));
        await AssertCountsAsync(0, 0);
    }

    [Fact]
    public async Task CancellationBeforeDownload()
    {
        var claim = await ClaimAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Processor(() => throw new InvalidOperationException("No source should be opened.")).ProcessAsync(claim, cancellation.Token));
        await AssertCountsAsync(0, 0);
    }

    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with { ProcessingEnabled = true };
    private DocumentIngestionInboxService Inbox() => new(_clock, _factory);
    private DocumentIngestionJobService Jobs() => new(Options.Create(Config), _clock, _factory);
    private DocumentPreparationStoreService Store() => new(_clock, _factory);
    private Task AcceptAsync() => Inbox().AcceptAsync(DocumentIngestionTestData.Notification(), TestContext.Current.CancellationToken);

    private async Task<DocumentIngestionClaim> ClaimAsync()
    {
        await AcceptAsync();
        return Assert.IsType<DocumentIngestionClaim>(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
    }

    private async Task<PdfPreparationResult> PrepareAsync(DocumentChunkingConfig? settings = null)
    {
        using var source = new MemoryStream(_bytes);
        return await new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), _chunker)
            .PrepareAsync(source, settings ?? new(), TestContext.Current.CancellationToken);
    }

    private PdfDocumentProcessor Processor(Func<Stream> openSource, DocumentPreparationConfig? config = null) => new(
        DocumentHybridPreparationTestData.Downloader(openSource, Config),
        new PdfDocumentPreparationService(Options.Create(config ?? new()), _chunker), Store());

    private async Task AssertCountsAsync(int generations, int chunks)
    {
        await using var db = _factory.CreateDbContext();
        Assert.Equal(generations, await db.DocumentPreparationGenerations.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(chunks, await db.DocumentTextChunks.CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }

    private sealed class SaveCheckpoint : SaveChangesInterceptor
    {
        public Action? OnSaved { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            OnSaved?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InterruptedStream(byte[] bytes, int status) : MemoryStream(bytes)
    {
        private bool _started;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_started)
                return ValueTask.FromException<int>(status == -1 ? new IOException("Synthetic read failure.")
                    : new RequestFailedException(status, "Synthetic range failure."));
            _started = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 16)], cancellationToken);
        }
    }
}
#endif
