#if DEBUG
using CasCap.Constants;
using CasCap.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Checks explicit receipt-derived review storage with SQLite, not PostgreSQL locks, JSONB or live preparation.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PreparedDocuments")]
public sealed class DocumentMarkdownGenerationTests : IAsyncLifetime
{
    private const string Markdown = DocumentHybridPreparationTestData.ReviewMarkdown;
    private static readonly string[] Changes = ["change-note"];
    private static readonly string[] Uncertainties = ["uncertainty-note"];
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly DocumentHybridPreparationTestData.Provider _provider = new();
    private readonly Checkpoint _checkpoint = new();
    private Factory _factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with { ProcessingEnabled = true };

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Token);
        _factory = new(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).AddInterceptors(_checkpoint).Options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(Token);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task StoreReadAndRepeatPreserveReviewOnlySnapshot()
    {
        var source = await SourceAsync();
        var receipts = await Receipts().ReadAsync(source, Token);
        var review = Review();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), review, Token));
        Assert.Equal(id, await Store().StoreAsync(source, new(), review, Token));
        var snapshot = Assert.IsType<DocumentMarkdownReviewGeneration>(await Store().ReadAsync(source, id, Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, snapshot.State);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal(review.Id, snapshot.Projection.Review!.Id);
        var projection = new DocumentChunkingService().ChunkMarkdown([new(1, Markdown)], new(), new(), review, Token);
        Assert.Equal(JsonSerializer.Serialize(projection, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(snapshot.Projection, JsonSerializerOptions.Web));
        Assert.DoesNotContain("inputTokenCount", JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal(["change-note"], Assert.Single(snapshot.Candidates).Changes);
        Assert.Equal(["uncertainty-note"], snapshot.Candidates[0].Uncertainties);
        Assert.Contains("- First\n- Second", Assert.Single(snapshot.Projection.Chunks).EmbeddingText, StringComparison.Ordinal);
        Assert.Equal(["# Control", "## C"], snapshot.Projection.Chunks[0].HeadingPath.Select(item => item.Markdown));
        Assert.DoesNotContain("change-note", snapshot.Projection.Chunks[0].EmbeddingText, StringComparison.Ordinal);
        Assert.DoesNotContain("uncertainty-note", snapshot.Projection.Chunks[0].EmbeddingText, StringComparison.Ordinal);
        Assert.Equal(receipts.Select(item => item.ResultJson), (await Receipts().ReadAsync(source, Token)).Select(item => item.ResultJson));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(DocumentIngestionStates.Queued, (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedReviewOrProfileAppendsWithoutReplacingEarlierGeneration(bool profile)
    {
        var source = await SourceAsync();
        var first = await Store().StoreAsync(source, new(), Review(), Token);
        var second = await Store().StoreAsync(source, profile ? new() { TargetTokensPerChunk = 128 } : new(),
            profile ? Review() : Review() with { ReviewId = "second-review" }, Token);
        Assert.NotEqual(first, second);
        Assert.NotNull(await Store().ReadAsync(source, first!, Token));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.DocumentMarkdownGenerations.CountAsync(Token));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    [InlineData("sequencer")]
    [InlineData("etag")]
    [InlineData("blob")]
    [InlineData("plan")]
    public async Task SourceScopeFencesWritesAndReads(string field)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        var forged = field switch
        {
            "tenant" => source with { TenantId = "tenant-b" },
            "collection" => source with { CollectionId = "other-collection" },
            "document" => source with { DocumentId = "other-document" },
            "job" => source with { JobId = "other-job" },
            "sequencer" => source with { Sequencer = "2".PadLeft(128, '0') },
            "etag" => source with { ETag = "other-etag" },
            "blob" => source with { BlobUri = "https://storage.example.com/documents/other.pdf" },
            "plan" => source with { PreparationPlanJson = "{}" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.Null(await Store().StoreAsync(forged, new(), Review(), Token));
        Assert.Null(await Store().ReadAsync(forged, id, Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersessionAndDeletionHideExistingGenerations(bool deleted)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        await new DocumentIngestionInboxService(TimeProvider.System, _factory).AcceptAsync(
            DocumentIngestionTestData.Notification("later", "2", deleted), Token);
        Assert.Null(await Store().ReadAsync(source, id, Token));
        Assert.Null(await Store().StoreAsync(source, new(), Review(), Token));
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("reconciliation")]
    [InlineData("completed")]
    public async Task OnlyFinishedReviewRequiredJobsCanStore(string state)
    {
        var source = await SourceAsync(finish: false);
        if (state != "processing")
            Assert.True(await Jobs().FinishAsync(source, state == "completed" ? DocumentProcessingOutcome.Completed
                : DocumentProcessingOutcome.ReconciliationRequired, Token));
        Assert.Null(await Store().StoreAsync(source, new(), Review(), Token));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("status")]
    [InlineData("source")]
    [InlineData("plan")]
    [InlineData("hash")]
    [InlineData("response")]
    [InlineData("stage")]
    [InlineData("pages")]
    [InlineData("layout-pages")]
    [InlineData("layout-characters")]
    [InlineData("layout-null")]
    public async Task IncompleteOrMismatchedReceiptsReject(string invalid)
    {
        var source = await SourceAsync();
        await using (var db = _factory.CreateDbContext())
        {
            var agent = await db.DocumentPreparationOperations.SingleAsync(item => item.Stage == DocumentPreparationOperations.PreparationAgent, Token);
            switch (invalid)
            {
                case "missing": db.Remove(agent); break;
                case "status": agent.Status = DocumentPreparationOperations.Received; break;
                case "source": agent.SourceSha256 = new string('B', 64); break;
                case "plan": agent.PlanSha256 = new string('B', 64); break;
                case "hash": agent.ResultSha256 = null; break;
                case "response": agent.ResultJson = "{}"; break;
                case "stage": agent.Stage = "unsupported"; break;
                case "pages": agent.PageNumber = 2; break;
                case "layout-pages":
                case "layout-characters":
                case "layout-null":
                    var managed = await db.DocumentPreparationOperations.SingleAsync(
                        item => item.Stage == DocumentPreparationOperations.DocumentIntelligence, Token);
                    var layout = JsonSerializer.Deserialize<DocumentLayoutPollResult>(managed.ResultJson!, JsonSerializerOptions.Web)!;
                    layout = invalid switch
                    {
                        "layout-pages" => layout with { Pages = new Dictionary<int, string> { [0] = "Invalid physical page." } },
                        "layout-characters" => layout with { Markdown = new string('x', 513) },
                        _ => layout with { Pages = null! },
                    };
                    managed.ResultJson = JsonSerializer.Serialize(layout, JsonSerializerOptions.Web);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            await db.SaveChangesAsync(Token);
        }
        if (invalid == "response")
            await Assert.ThrowsAsync<JsonException>(() => Store().StoreAsync(source, new(), Review(), Token));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => Store(invalid == "layout-characters"
                ? new() { MaximumExtractedCharacters = 512 } : null).StoreAsync(source, new(), Review(), Token));
        await using var check = _factory.CreateDbContext();
        Assert.Empty(await check.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Fact]
    public async Task SaveFailureRollsBackSnapshot()
    {
        var source = await SourceAsync();
        _checkpoint.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => Store().StoreAsync(source, new(), Review(), Token));
        _checkpoint.Fail = false;
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonFormattingDoesNotChangeTypedIdentity(bool receipts)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        await using (var db = _factory.CreateDbContext())
        {
            if (receipts)
                foreach (var receipt in await db.DocumentPreparationOperations.ToArrayAsync(Token))
                {
                    receipt.ResultJson = Reformat(receipt.ResultJson!);
                    Assert.NotEqual(receipt.ResultSha256, DocumentPreparationIdentity.Hash(receipt.ResultJson));
                }
            else
            {
                var row = await db.DocumentMarkdownGenerations.SingleAsync(Token);
                row.PayloadJson = Reformat(row.PayloadJson);
            }
            await db.SaveChangesAsync(Token);
        }
        Assert.NotNull(await Store().ReadAsync(source, id, Token));
        Assert.Equal(id, await Store().StoreAsync(source, new(), Review(), Token));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("projection")]
    [InlineData("sourceSha256")]
    public async Task CorruptSnapshotsRejectReadsAndRetries(string field)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        await using (var db = _factory.CreateDbContext())
        {
            var row = await db.DocumentMarkdownGenerations.SingleAsync(Token);
            using var json = JsonDocument.Parse(row.PayloadJson);
            var properties = json.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
            properties[field] = field switch
            {
                "version" => JsonSerializer.SerializeToElement(2),
                "projection" => JsonSerializer.SerializeToElement<object?>(null),
                _ => JsonSerializer.SerializeToElement(new string('B', 64)),
            };
            row.PayloadJson = JsonSerializer.Serialize(properties);
            await db.SaveChangesAsync(Token);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().ReadAsync(source, id, Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().StoreAsync(source, new(), Review(), Token));
    }

    [Fact]
    public async Task ReleasedLeaseDoesNotAuthorizeOrBlockReviewStorage()
    {
        var source = await SourceAsync();
        var expired = source with { ClaimId = Guid.Empty, ExpiresAtUtc = DateTimeOffset.MinValue };
        var id = Assert.IsType<string>(await Store().StoreAsync(expired, new(), Review(), Token));
        Assert.NotNull(await Store().ReadAsync(expired, id, Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InconsistentStoredSourceMetadataCannotClaimIdempotentSuccess(bool etag)
    {
        var source = await SourceAsync();
        var id = Assert.IsType<string>(await Store().StoreAsync(source, new(), Review(), Token));
        await using (var db = _factory.CreateDbContext())
        {
            var row = await db.DocumentMarkdownGenerations.SingleAsync(Token);
            if (etag)
                row.ETag = "other-etag";
            else
                row.Sequencer = "2";
            await db.SaveChangesAsync(Token);
        }
        Assert.Null(await Store().ReadAsync(source, id, Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().StoreAsync(source, new(), Review(), Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateAndNoteBudgetHasExactAggregateBoundary(bool withinLimit)
    {
        var markdown = "# Control\n\n" + new string('x', 1_000);
        var source = await SourceAsync(pages: 2, markdown: markdown);
        var exact = 2 * (markdown.Length + Changes.Sum(note => note.Length) + Uncertainties.Sum(note => note.Length));
        var store = Store(new() { MaximumExtractedCharacters = exact - (withinLimit ? 0 : 1) });
        if (withinLimit)
        {
            var id = Assert.IsType<string>(await store.StoreAsync(source, new(), null, Token));
            Assert.Equal(2, (await store.ReadAsync(source, id, Token))!.Candidates.Count);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.StoreAsync(source, new(), null, Token));
            Assert.Contains("generation character limit", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("pages")]
    [InlineData("chunks")]
    public async Task ResourceBoundsRejectWholeGeneration(string bound)
    {
        var source = await SourceAsync(pages: 2);
        var limits = bound switch
        {
            "receipts" => new DocumentPreparationConfig { MaximumExtractedCharacters = 1 },
            "pages" => new DocumentPreparationConfig { MaximumPages = 1 },
            "chunks" => new DocumentPreparationConfig { MaximumChunks = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(bound)),
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(limits).StoreAsync(source, new(), null, Token));
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationLeavesNoPartialGeneration(bool afterInsert)
    {
        var source = await SourceAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (afterInsert)
            _checkpoint.AfterSave = (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
        else
            cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store().StoreAsync(source, new(), Review(), cancellation.Token));
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Fact]
    public async Task SourceRecheckRollsBackGenerationAndConcurrentChange()
    {
        var source = await SourceAsync();
        _checkpoint.AfterSave = (db, token) => db.KnowledgeDocuments.ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.Sequencer, "2"), token);
        Assert.Null(await Store().StoreAsync(source, new(), Review(), Token));
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
        Assert.Equal(source.Sequencer, (await db.KnowledgeDocuments.SingleAsync(Token)).Sequencer);
    }

    [Fact]
    public async Task ReceiptRecheckRejectsChangesBeforeMutation()
    {
        var source = await SourceAsync();
        var created = 0;
        _factory.OnCreate = db =>
        {
            if (++created == 2)
                db.DocumentPreparationOperations.Where(item => item.Stage == DocumentPreparationOperations.PreparationAgent)
                    .ExecuteUpdate(setters => setters.SetProperty(item => item.ResultJson, "{}"));
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().StoreAsync(source, new(), Review(), Token));
        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Fact]
    public async Task CrossTenantForeignKeyRejectsForgedOwnership()
    {
        var source = await SourceAsync();
        await Store().StoreAsync(source, new(), Review(), Token);
        await using var db = _factory.CreateDbContext();
        var existing = await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token);
        existing.Id = new string('B', 64);
        existing.TenantId = "tenant-b";
        db.Add(existing);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
    }

    [Fact]
    public void ReviewSourcePredicateTranslatesForPostgreSqlWithoutConnecting()
    {
        using var db = new AgentRuntimeDbContext(new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql("Host=invalid.example;Database=design;Username=design").Options);
        var sql = DocumentIngestionTransactions.CurrentReviewSource(db, new()
        {
            JobId = "job",
            ClaimId = Guid.Empty,
            TenantId = "tenant-a",
            CollectionId = "collection",
            DocumentId = "document",
            Sequencer = "1",
            BlobUri = "https://storage.example.com/documents/source.pdf",
            ETag = "etag",
            ExpiresAtUtc = DateTimeOffset.MinValue,
            PreparationPlanJson = "{}",
        }).ToQueryString();
        Assert.Contains("document_ingestion_jobs", sql, StringComparison.Ordinal);
        Assert.Contains("knowledge_documents", sql, StringComparison.Ordinal);
        Assert.Contains("finished_at_utc", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("claim_expires_at_utc >", sql, StringComparison.Ordinal);
    }

    private Task<DocumentIngestionClaim> SourceAsync(bool finish = true, int pages = 1, string markdown = Markdown) =>
        DocumentHybridPreparationTestData.PrepareReviewAsync(_factory, _provider, Token, finish, pages, markdown);

    private static DocumentMarkdownSourceReview Review() => DocumentHybridPreparationTestData.SourceReview();

    private static string Reformat(string value)
    {
        using var json = JsonDocument.Parse(value);
        return JsonSerializer.Serialize(Reordered(json.RootElement), IndentedJsonOptions);
    }

    private static object Reordered(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Reverse().ToDictionary(item => item.Name, item => Reordered(item.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Reordered).ToArray(),
        _ => value.Clone(),
    };

    private DocumentPreparationMarkdownStore Store(DocumentPreparationConfig? limits = null) =>
        new(Options.Create(limits ?? new DocumentPreparationConfig()), TimeProvider.System, _factory, new());
    private DocumentPreparationOperationStore Receipts() => new(TimeProvider.System, _factory);
    private DocumentIngestionJobService Jobs() => new(Options.Create(Config), TimeProvider.System, _factory);

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public Action<AgentRuntimeDbContext>? OnCreate { get; set; }

        public AgentRuntimeDbContext CreateDbContext()
        {
            var db = new AgentRuntimeDbContext(options);
            OnCreate?.Invoke(db);
            return db;
        }
    }

    private sealed class Checkpoint : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public Func<AgentRuntimeDbContext, CancellationToken, Task>? AfterSave { get; set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new IOException("Synthetic post-insert failure.");
            if (AfterSave is not null && eventData.Context is AgentRuntimeDbContext db)
                await AfterSave(db, cancellationToken);
            return result;
        }
    }
}
#endif
