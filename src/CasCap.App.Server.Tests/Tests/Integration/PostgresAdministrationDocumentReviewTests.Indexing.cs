#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indexing_PreflightRechecksAfterNativeMutationLock(bool revokeSource)
    {
        await _host.DisposeAsync();
        _host = new(_database.DbContextFactory, indexing: DocumentIndexingTestData.Config(_source.TenantId, _source.CollectionId));
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new());
        var snapshot = await NativeSelectedSnapshotAsync(client);
        var id = snapshot.GenerationId!;
        Assert.True((await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, NativeApprovalRequest(), Token)).IsSuccess);
        var eligible = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.True(eligible.Value!.Eligible);
        await using var db = _database.DbContextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(db, Token);
        var pending = http.GetAsync(Path(DocumentReviewRoutes.IndexingPreflight, id), Token);
        await _database.WaitForBlockedAdvisoryLockAsync(Token);
        if (revokeSource)
            (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
        else
        {
            var selection = await db.DocumentChunkReviews.SingleAsync(Token);
            db.DocumentChunkReviews.Add(new()
            {
                TenantId = selection.TenantId,
                GenerationId = selection.GenerationId,
                Revision = 2,
                ActorId = "example-reviewer",
                ExclusionsJson = selection.ExclusionsJson,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        using var response = await pending;
        if (revokeSource)
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<CasCap.Client.Models.DocumentIndexingPreflightResponse>(Token);
            Assert.False(result!.Eligible);
            Assert.Null(result.ManifestSha256);
            Assert.Equal(2, result.ChunkReviewRevision);
        }
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Single(await db.DocumentApprovals.ToArrayAsync(Token));
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
    }
}
#endif
