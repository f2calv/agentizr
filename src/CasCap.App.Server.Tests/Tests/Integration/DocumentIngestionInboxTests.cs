#if DEBUG
using CasCap.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Verifies relational admission state; SQLite does not validate PostgreSQL locks or races.</summary>
[Trait("Category", "Ingestion")]
[Trait("Category", "Integration")]
public sealed class DocumentIngestionInboxTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private Factory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        _factory = new Factory(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).Options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task DuplicateDeliveryCreatesOneJob()
    {
        var inbox = Create();
        var notification = DocumentIngestionTestData.Notification();
        await inbox.AcceptAsync(notification, TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(notification, TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentIngestionJobs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentIngestionStates.Queued,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task EventIdentityCannotBeReused()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(() =>
            inbox.AcceptAsync(DocumentIngestionTestData.Notification(sequencer: "2"), TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentIngestionJobs.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OlderNotificationCannotReplaceNewerState()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("new", "10"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("old", "f"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        var document = await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("10".PadLeft(128, '0'), document.Sequencer);
        Assert.Equal(1, await db.DocumentIngestionJobs.CountAsync(job => job.Status == DocumentIngestionStates.Ignored,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSupersedesJobsAndPreventsResurrection()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("create", "1"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("delete", "2", deleted: true), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("overwrite", "3"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.Deleted,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, await db.DocumentIngestionJobs.CountAsync(job => job.Status == DocumentIngestionStates.Queued,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CollectionsAndTenantsHaveSeparateDocuments()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("a"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("b", tenant: "tenant-b"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.KnowledgeDocuments.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.KnowledgeDocuments.CountAsync(document => document.TenantId == "tenant-a",
            TestContext.Current.CancellationToken));
    }

    private DocumentIngestionInboxService Create() => new(TimeProvider.System, _factory);

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }
}
#endif
