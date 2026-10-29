#if DEBUG
using Azure;
using CasCap.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the same synchronization contract on SQLite and isolated migrated PostgreSQL databases.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentSynchronization")]
public sealed class DocumentSynchronizationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with
    {
        SynchronizationEnabled = true,
        ProcessingEnabled = true,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MissingOriginalAndRepeatedRequests(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var inventory = new Inventory();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.RequestAsync("tenant-a", Token);
        Assert.True(await service.ProcessNextAsync(Token));
        Assert.False(await service.ProcessNextAsync(Token));
        await using (var db = database.CreateDbContext())
        {
            var document = await db.KnowledgeDocuments.SingleAsync(Token);
            Assert.Equal(new string('0', 128), document.Sequencer);
            Assert.Equal(inventory.Original.ETag, document.ETag);
            Assert.NotNull(document.LastSeenSyncId);
            Assert.Equal(0, (await db.DocumentIngestionJobs.SingleAsync(Token)).AttemptCount);
            Assert.Equal(1, (await db.DocumentSynchronizations.SingleAsync(Token)).AddedCount);
        }
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var result = database.CreateDbContext();
        Assert.Single(await result.DocumentIngestionJobs.ToArrayAsync(Token));
        var state = await result.DocumentSynchronizations.SingleAsync(Token);
        Assert.Equal(1, state.UnchangedCount);
        Assert.Equal(DocumentSynchronizationStates.Succeeded, state.State);
        Assert.NotNull(state.LastSucceededAtUtc);
        Assert.Empty(await result.DocumentPreparationOperations.ToArrayAsync(Token));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MissingWorkAndUnattemptedPlanRepair(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        await new DocumentIngestionInboxService(TimeProvider.System, database)
            .AcceptAsync(DocumentIngestionTestData.Notification(), Token);
        await DocumentHybridPreparationTestData.PublishAsync(database, TimeProvider.System, Token);
        var config = Config with
        {
            ProcessingEnabled = false,
            Sources = [Config.Sources[0] with { AgentPreparation = DocumentHybridPreparationTestData.Plan.Settings }],
        };
        var service = Create(database, new Inventory(), config);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var db = database.CreateDbContext();
        var job = await db.DocumentIngestionJobs.SingleAsync(Token);
        Assert.NotNull(job.PreparationPlanJson);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(1, (await db.DocumentSynchronizations.SingleAsync(Token)).RepairedCount);
        Assert.Null(await new DocumentIngestionJobService(Options.Create(config), TimeProvider.System, database).ClaimAsync(Token));
        Assert.Empty(await db.DocumentPreparationOperations.ToArrayAsync(Token));
        db.DocumentIngestionJobs.Remove(job);
        await db.SaveChangesAsync(Token);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var repaired = database.CreateDbContext();
        Assert.NotEqual(job.Id, (await repaired.DocumentIngestionJobs.SingleAsync(Token)).Id);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConfirmedDeletionPurgesChunksAndFencesActiveWriter(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var claim = await ClaimAsync(database);
        var preparation = await PreparationAsync();
        var store = new DocumentPreparationStoreService(TimeProvider.System, database);
        Assert.True(await store.StoreAsync(claim, preparation, Token));
        var inventory = new Inventory();
        inventory.Items.Clear();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        Assert.False(await store.StoreAsync(claim, preparation, Token));
        Assert.False(await Jobs(database).FinishAsync(claim, DocumentProcessingOutcome.Completed, Token));
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.Deleted, (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Equal(1, (await db.DocumentSynchronizations.SingleAsync(Token)).RemovedCount);
        Assert.Null((await db.DocumentIngestionJobs.SingleAsync(Token)).ClaimId);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeletionPurgesReviewContentButPreservesUsageAudit(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var provider = new DocumentHybridPreparationTestData.Provider();
        var source = await DocumentHybridPreparationTestData.PrepareReviewAsync(database, provider, Token);
        var store = new DocumentPreparationMarkdownStore(Options.Create(new DocumentPreparationConfig()),
            TimeProvider.System, database, new DocumentChunkingService());
        Assert.NotNull(await store.StoreAsync(source, new(), null, Token));
        var inventory = new Inventory();
        inventory.Items.Clear();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
        var receipts = await db.DocumentPreparationOperations.ToArrayAsync(Token);
        Assert.NotEmpty(receipts);
        Assert.All(receipts, item => Assert.Null(item.ResultJson));
        Assert.All(receipts, item => Assert.NotNull(item.ResultSha256));
        Assert.Null(await store.PreviewAsync(source, Token));
        Assert.Equal(1, provider.Submissions);
        Assert.Equal(1, provider.ModelCalls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PartialInventoryNeverInfersDeletion(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var claim = await ClaimAsync(database);
        Assert.True(await new DocumentPreparationStoreService(TimeProvider.System, database)
            .StoreAsync(claim, await PreparationAsync(), Token));
        var inventory = new Inventory { FailListing = true };
        inventory.Items.Clear();
        inventory.Items["https://storage.example.com/documents/another.pdf"] =
            new("https://storage.example.com/documents/another.pdf", "another-etag");
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var db = database.CreateDbContext();
        Assert.NotEmpty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.NotEqual(DocumentIngestionStates.Deleted,
            (await db.KnowledgeDocuments.SingleAsync(item => item.Id == claim.DocumentId, Token)).Status);
        var state = await db.DocumentSynchronizations.SingleAsync(Token);
        Assert.Equal(DocumentSynchronizationStates.Failed, state.State);
        Assert.Equal(DocumentSynchronizationStates.StorageUnavailable, state.FailureCode);
        Assert.Null(state.LastSucceededAtUtc);
    });

    [Theory]
    [InlineData(false, 403)]
    [InlineData(false, 404)]
    [InlineData(false, 503)]
    [InlineData(true, 403)]
    [InlineData(true, 404)]
    [InlineData(true, 503)]
    public Task MetadataFailuresAreNotDeletions(bool postgres, int status) => WithDatabaseAsync(postgres, async database =>
    {
        var claim = await ClaimAsync(database);
        var inventory = new Inventory { MetadataFailure = status };
        inventory.Items.Clear();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var db = database.CreateDbContext();
        Assert.NotEqual(DocumentIngestionStates.Deleted, (await db.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Equal(DocumentSynchronizationStates.Failed, (await db.DocumentSynchronizations.SingleAsync(Token)).State);
        Assert.True(await Jobs(database).FinishAsync(claim, DocumentProcessingOutcome.Completed, Token));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InventoryRepairsMissedChangeAndIgnoresDelayedEvents(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var inventory = new Inventory();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        var claim = Assert.IsType<DocumentIngestionClaim>(await Jobs(database).ClaimAsync(Token));
        Assert.True(await new DocumentPreparationStoreService(TimeProvider.System, database)
            .StoreAsync(claim, await PreparationAsync(), Token));
        inventory.Items[inventory.Original.BlobUri] = inventory.Original with { ETag = "replacement-etag" };
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        var inbox = new DocumentIngestionInboxService(TimeProvider.System, database, inventory);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("late-create", "2"), Token);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("late-delete", "3", deleted: true), Token);
        Assert.False(await Jobs(database).FinishAsync(claim, DocumentProcessingOutcome.Completed, Token));
        await using var db = database.CreateDbContext();
        Assert.Equal("replacement-etag", (await db.KnowledgeDocuments.SingleAsync(Token)).ETag);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Single(await db.DocumentIngestionJobs.Where(item => item.Status == DocumentIngestionStates.Queued).ToArrayAsync(Token));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SameRevisionEventAndTombstoneAreRepeatSafe(bool postgres) => WithDatabaseAsync(postgres, async database =>
    {
        var inventory = new Inventory();
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        var inbox = new DocumentIngestionInboxService(TimeProvider.System, database, inventory);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("late-event", "a"), Token);
        await using (var db = database.CreateDbContext())
            Assert.Single(await db.DocumentIngestionJobs.Where(item => item.Status == DocumentIngestionStates.Queued).ToArrayAsync(Token));
        inventory.Items.Clear();
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        inventory.Items[inventory.Original.BlobUri] = inventory.Original;
        await service.RequestAsync("tenant-a", Token);
        await service.ProcessNextAsync(Token);
        await using var result = database.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.Deleted, (await result.KnowledgeDocuments.SingleAsync(Token)).Status);
        Assert.Equal(1, (await result.DocumentSynchronizations.SingleAsync(Token)).ConflictCount);
        Assert.Empty(await result.DocumentIngestionJobs.Where(item => item.Status == DocumentIngestionStates.Queued).ToArrayAsync(Token));
    });

    [Fact]
    public Task UnknownTenantCannotRequestAnotherApplication() => WithDatabaseAsync(false, async database =>
    {
        var service = Create(database, new Inventory());
        await service.RequestAsync("another-tenant", Token);
        Assert.False(await service.ProcessNextAsync(Token));
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.DocumentSynchronizations.ToArrayAsync(Token));
        Assert.Empty(await db.KnowledgeDocuments.ToArrayAsync(Token));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestAsync("tenant-a", cancellation.Token));
    });

    [Fact]
    public async Task AdditiveMigrationPreservesExistingAdmission()
    {
        await using var database = new PostgreSqlFixture { InitialMigration = "20261028120307_DocumentMarkdownReviewGenerations" };
        await database.InitializeAsync();
        await using (var db = database.DbContextFactory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO agent_runtime.knowledge_documents
                    (id, tenant_id, collection_id, blob_uri, sequencer, etag, status, updated_at_utc)
                VALUES ('synthetic-document', 'tenant-a', 'manuals',
                    'https://storage.example.com/documents/existing.pdf', '0', 'existing-etag', 'Queued', now())
                """, Token);
            await db.Database.MigrateAsync(Token);
        }
        await using var result = database.DbContextFactory.CreateDbContext();
        var document = await result.KnowledgeDocuments.SingleAsync(Token);
        Assert.Equal("existing-etag", document.ETag);
        Assert.Null(document.LastSeenSyncId);
        Assert.Empty(await result.DocumentSynchronizations.ToArrayAsync(Token));
    }

    [Fact]
    public Task ConcurrentOwnersAndExpiredRunCannotPublish() => WithDatabaseAsync(true, async database =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var inventory = new Inventory
        {
            BeforeRead = async token =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
            },
        };
        var service = Create(database, inventory);
        await service.RequestAsync("tenant-a", Token);
        var first = service.ProcessNextAsync(Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.False(await Create(database, inventory).ProcessNextAsync(Token));
            await using (var db = database.CreateDbContext())
            {
                var state = await db.DocumentSynchronizations.SingleAsync(Token);
                state.ExpiresAtUtc = DateTimeOffset.UnixEpoch;
                await db.SaveChangesAsync(Token);
            }
            inventory.Items[inventory.Original.BlobUri] = inventory.Original with { ETag = "current-etag" };
            Assert.True(await Create(database, inventory).ProcessNextAsync(Token));
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
        await using var result = database.CreateDbContext();
        Assert.Equal("current-etag", (await result.KnowledgeDocuments.SingleAsync(Token)).ETag);
        Assert.Single(await result.DocumentIngestionJobs.ToArrayAsync(Token));
        Assert.Equal(DocumentSynchronizationStates.Succeeded, (await result.DocumentSynchronizations.SingleAsync(Token)).State);
    });

    private static DocumentSynchronizationService Create(
        IDbContextFactory<AgentRuntimeDbContext> database, Inventory inventory, DocumentIngestionConfig? config = null) =>
        new(NullLogger<DocumentSynchronizationService>.Instance, Options.Create(config ?? Config), TimeProvider.System, database, inventory);

    private static DocumentIngestionJobService Jobs(IDbContextFactory<AgentRuntimeDbContext> database) =>
        new(Options.Create(Config), TimeProvider.System, database);

    private static async Task<DocumentIngestionClaim> ClaimAsync(IDbContextFactory<AgentRuntimeDbContext> database)
    {
        await new DocumentIngestionInboxService(TimeProvider.System, database).AcceptAsync(DocumentIngestionTestData.Notification(), Token);
        return Assert.IsType<DocumentIngestionClaim>(await Jobs(database).ClaimAsync(Token));
    }

    private static async Task<PdfPreparationResult> PreparationAsync()
    {
        using var pdf = new MemoryStream(DocumentPreparationTestData.Pdf("Synthetic source content."));
        return await new PdfDocumentPreparationService(Options.Create(new DocumentPreparationConfig()), new DocumentChunkingService())
            .PrepareAsync(pdf, new(), Token);
    }

    private static async Task WithDatabaseAsync(bool postgres, Func<IDbContextFactory<AgentRuntimeDbContext>, Task> test)
    {
        if (postgres)
        {
            await using var database = new PostgreSqlFixture();
            await database.InitializeAsync();
            await test(database.DbContextFactory);
            return;
        }
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync(Token);
        var factory = new Factory(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
            await db.Database.EnsureCreatedAsync(Token);
        await test(factory);
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }

    private sealed class Inventory : IDocumentBlobInventory
    {
        public DocumentBlobMetadata Original { get; } = new("https://storage.example.com/documents/manual.pdf", "\"example-etag\"");

        public Dictionary<string, DocumentBlobMetadata> Items { get; } = new()
        {
            ["https://storage.example.com/documents/manual.pdf"] =
                new("https://storage.example.com/documents/manual.pdf", "\"example-etag\""),
        };

        public bool FailListing { get; init; }

        public int? MetadataFailure { get; init; }

        public Func<CancellationToken, Task>? BeforeRead { get; init; }

        public async IAsyncEnumerable<DocumentBlobMetadata> ListAsync(
            DocumentSourceConfig source, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            foreach (var item in Items.Values.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (item.BlobUri.StartsWith(source.ContainerUri + "/", StringComparison.Ordinal))
                    yield return item;
            }
            if (FailListing)
                throw new RequestFailedException(503, "Synthetic incomplete listing.");
        }

        public async Task<DocumentBlobMetadata?> ReadAsync(string blobUri, CancellationToken token)
        {
            if (MetadataFailure is { } status)
                throw new RequestFailedException(status, "Synthetic metadata failure.", "ContainerNotFound", null);
            Items.TryGetValue(blobUri, out var item);
            if (BeforeRead is { } before)
                await before(token);
            return item;
        }
    }
}
#endif
