#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;

namespace CasCap.IntegrationTests;

public sealed partial class AdministrationDocumentReviewTests
{
    private static DocumentIndexingRequest IndexingRequest() => new()
    {
        Confirmed = true,
        ManifestSha256 = new('0', 64),
        MaximumRequests = 1,
        MaximumInputTokens = 100,
        ExpectedActiveJobId = null,
    };

    [Fact]
    public async Task Indexing_DefaultRemainsDisabledAndNonAuthorizing()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var result = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, Token);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Eligible);
        Assert.False(result.Value.IndexingApproved);
        Assert.Contains(result.Value.Blockers, blocker => blocker.Contains("disabled", StringComparison.Ordinal));
        Assert.Null(result.Value.ManifestSha256);
    }

    [Fact]
    public async Task Indexing_PreflightTracksAssessmentSelectionAndSourceWithoutMutations()
    {
        await _host.DisposeAsync();
        _host = new(_database, indexing: DocumentIndexingTestData.Config(_source.TenantId, _source.CollectionId));
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var id = snapshot.GenerationId!;
        var before = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.False(before.Value!.Eligible);
        Assert.True((await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, ApprovalRequest(), Token)).IsSuccess);
        var first = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.Eligible);
        Assert.False(first.Value.IndexingApproved);
        Assert.Equal(snapshot.Projection.GetProperty("chunks").GetArrayLength(), first.Value.IncludedChunkCount);
        using var raw = await http.GetAsync(Path(DocumentReviewRoutes.IndexingPreflight, id), Token);
        Assert.True(raw.Headers.CacheControl?.NoStore);
        var json = await raw.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("embeddingText", json, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("notes", json, StringComparison.OrdinalIgnoreCase);
        Assert.True((await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, SelectionRequest() with
            {
                ExpectedRevision = 1,
                Exclusions = [new() { Ordinal = 0 }],
            }, Token)).IsSuccess);
        var changed = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.False(changed.Value!.Eligible);
        Assert.Null(changed.Value.ManifestSha256);
        await using var db = _database.CreateDbContext();
        Assert.Single(await db.DocumentApprovals.ToArrayAsync(Token));
        Assert.Equal(2, await db.DocumentChunkReviews.CountAsync(Token));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
        await db.SaveChangesAsync(Token);
        var deleted = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, Token);
        Assert.Equal(ApiStatus.NotFound, deleted.Status);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    [InlineData("snapshot")]
    public async Task Indexing_PreflightChecksEveryOwnershipDimension(string dimension)
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var result = await client.GetDocumentIndexingPreflightAsync(
            dimension == "app" ? "default" : "tenant-a",
            dimension == "collection" ? "other" : _source.CollectionId,
            dimension == "document" ? "other" : _source.DocumentId,
            dimension == "job" ? "other" : _source.JobId,
            dimension == "snapshot" ? "other" : snapshot.GenerationId!, Token);
        Assert.Equal(ApiStatus.NotFound, result.Status);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("etag")]
    [InlineData("sequence")]
    public async Task Indexing_SourceChangesMakeTheOldSnapshotUnavailable(string change)
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        await using var db = _database.CreateDbContext();
        var source = await db.KnowledgeDocuments.SingleAsync(Token);
        if (change == "deleted")
            source.Status = DocumentIngestionStates.Deleted;
        if (change == "etag")
            source.ETag = "\"replacement-etag\"";
        if (change == "sequence")
            source.Sequencer = "2".PadLeft(128, '0');
        await db.SaveChangesAsync(Token);
        var result = await client.GetDocumentIndexingPreflightAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, Token);
        Assert.Equal(ApiStatus.NotFound, result.Status);
    }
}
#endif
