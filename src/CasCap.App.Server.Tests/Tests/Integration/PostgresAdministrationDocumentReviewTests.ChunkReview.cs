#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeChunkReview_PreservesSnapshotsAndSerializesDecisions(bool upgrade)
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(stored.IsSuccess);
        var id = stored.Value!.GenerationId!;
        await using var db = _database.DbContextFactory.CreateDbContext();
        var original = (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson;
        var receipts = await ReceiptJsonAsync(db);
        if (upgrade)
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261029104131_AddDocumentSynchronization", Token);
            Assert.Equal(original, (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson);
            await migrator.MigrateAsync(cancellationToken: Token);
        }
        var request = NativeSelectionRequest() with
        {
            Exclusions = [new() { Ordinal = 0, Reason = "irrelevant" }],
        };
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.RecordDocumentChunkReviewAsync(
            "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id, request, Token)));
        Assert.All(writes, write => Assert.True(write.IsSuccess));
        Assert.All(writes, write => Assert.Equal(1, write.Value!.ChunkReview!.Revision));
        Assert.Single(writes.Select(write => write.Value!.ChunkReview!.RecordedAtUtc).Distinct());
        var decisions = await Task.WhenAll(new[]
        {
            request with { ExpectedRevision = 1, Exclusions = [] },
            request with { ExpectedRevision = 1, Exclusions = [new() { Ordinal = 0, Reason = "different decision" }] },
        }.Select(proposal => client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, proposal, Token)));
        Assert.Single(decisions, decision => decision.IsSuccess);
        Assert.Single(decisions, decision => decision.Status == ApiStatus.Conflict);
        var read = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, read.Value!.ChunkReview!.Revision);
        Assert.False(read.Value.ChunkReview.IndexingApproved);
        Assert.Equal(stored.Value.Projection.GetRawText(), read.Value.Projection.GetRawText());
        Assert.Equal(2, await db.DocumentChunkReviews.CountAsync(Token));
        Assert.Equal(original, (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson);
        Assert.Equal(receipts, await ReceiptJsonAsync(db));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(1, _provider.ModelCalls);
        Assert.Equal(1, _provider.Submissions);
    }

    [Fact]
    public async Task NativeChunkReview_RejectsSourceRevokedWhileWaitingForLock()
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var snapshot = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(snapshot.IsSuccess);
        await using var db = _database.DbContextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(db, Token);
        var pending = http.PutAsJsonAsync(Path(DocumentReviewRoutes.ChunkReview, snapshot.Value!.GenerationId),
            NativeSelectionRequest(), Token);
        await _database.WaitForBlockedAdvisoryLockAsync(Token);
        var document = await db.KnowledgeDocuments.SingleAsync(Token);
        document.Status = DocumentIngestionStates.Deleted;
        await db.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.DocumentChunkReviews.ToArrayAsync(Token));
    }

    private DocumentChunkReviewRequest NativeSelectionRequest() => new()
    {
        Sequencer = _source.Sequencer,
        ETag = _source.ETag!,
        ExpectedRevision = 0,
        Exclusions = [],
    };
}
#endif
