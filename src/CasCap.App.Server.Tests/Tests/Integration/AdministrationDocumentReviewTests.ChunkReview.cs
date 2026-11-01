#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.IntegrationTests;

public sealed partial class AdministrationDocumentReviewTests
{
    private static readonly JsonSerializerOptions IndentedJson = new(JsonSerializerOptions.Web) { WriteIndented = true };

    [Fact]
    public async Task ChunkReview_AppendsReadsRetriesAndPreservesArtifacts()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var snapshot = await StoredSnapshotAsync(client);
        await using var db = _database.CreateDbContext();
        var original = (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson;
        var receipts = await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id)
            .Select(item => item.ResultJson).ToArrayAsync(Token);
        Assert.Null(snapshot.ChunkReview);
        var request = SelectionRequest() with
        {
            Exclusions = [new() { Ordinal = 0, Reason = "  <script>irrelevant</script>  " }],
        };
        var first = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.True(first.IsSuccess);
        var saved = first.Value!.ChunkReview!;
        Assert.Equal(1, saved.Revision);
        Assert.False(saved.IndexingApproved);
        Assert.Equal("<script>irrelevant</script>", Assert.Single(saved.Exclusions).Reason);
        Assert.Equal(snapshot.Projection.GetRawText(), first.Value.Projection.GetRawText());
        Assert.Equal(snapshot.GenerationId, first.Value.GenerationId);
        var record = await db.DocumentChunkReviews.SingleAsync(Token);
        Assert.False(string.IsNullOrWhiteSpace(record.ActorId));
        Assert.Equal("tenant-a", record.TenantId);
        // JSONB rewrites whitespace; idempotency compares canonical decisions, not raw database JSON.
        record.ExclusionsJson = JsonSerializer.Serialize(saved.Exclusions, IndentedJson);
        await db.SaveChangesAsync(Token);
        var retry = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.True(retry.IsSuccess);
        Assert.Equal(saved.Revision, retry.Value!.ChunkReview!.Revision);
        Assert.Equal(saved.RecordedAtUtc, retry.Value.ChunkReview.RecordedAtUtc);
        var read = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId, Token);
        Assert.Equal(1, read.Value!.ChunkReview!.Revision);
        var changed = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, SelectionRequest() with { ExpectedRevision = 1 }, Token);
        Assert.True(changed.IsSuccess);
        Assert.Equal(2, changed.Value!.ChunkReview!.Revision);
        Assert.Empty(changed.Value.ChunkReview.Exclusions);
        var stale = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.Equal(ApiStatus.Conflict, stale.Status);
        var preview = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, null, Token);
        Assert.Null(preview.Value!.ChunkReview);
        Assert.Equal(2, await db.DocumentChunkReviews.CountAsync(Token));
        Assert.Equal(original, (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson);
        Assert.Equal(receipts, await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id)
            .Select(item => item.ResultJson).ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(1, _provider.ModelCalls);
        Assert.Equal(1, _provider.Submissions);
    }

    [Theory]
    [InlineData("unknown", HttpStatusCode.BadRequest)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("negative", HttpStatusCode.BadRequest)]
    [InlineData("long-reason", HttpStatusCode.BadRequest)]
    [InlineData("null-list", HttpStatusCode.BadRequest)]
    [InlineData("null-item", HttpStatusCode.BadRequest)]
    [InlineData("negative-revision", HttpStatusCode.BadRequest)]
    [InlineData("overflow-revision", HttpStatusCode.BadRequest)]
    [InlineData("terminal-predecessor", HttpStatusCode.Conflict)]
    [InlineData("etag", HttpStatusCode.Conflict)]
    [InlineData("sequence", HttpStatusCode.Conflict)]
    [InlineData("revision", HttpStatusCode.Conflict)]
    public async Task ChunkReview_RejectsInvalidOrStaleDecisions(string change, HttpStatusCode expected)
    {
        using var http = await ClientAsync();
        var snapshot = await StoredSnapshotAsync(new(http, new()));
        var request = SelectionRequest();
        request = change switch
        {
            "unknown" => request with { Exclusions = [new() { Ordinal = int.MaxValue }] },
            "duplicate" => request with { Exclusions = [new() { Ordinal = 0 }, new() { Ordinal = 0 }] },
            "negative" => request with { Exclusions = [new() { Ordinal = -1 }] },
            "long-reason" => request with { Exclusions = [new() { Ordinal = 0, Reason = new('x', 501) }] },
            "null-list" => request with { Exclusions = null! },
            "null-item" => request with { Exclusions = [null!] },
            "negative-revision" => request with { ExpectedRevision = -1 },
            "overflow-revision" => request with { ExpectedRevision = long.MaxValue },
            "terminal-predecessor" => request with { ExpectedRevision = long.MaxValue - 1 },
            "etag" => request with { ETag = "stale" },
            "sequence" => request with { Sequencer = "stale" },
            "revision" => request with { ExpectedRevision = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.ChunkReview, snapshot.GenerationId), request, Token);
        Assert.Equal(expected, response.StatusCode);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentChunkReviews.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ChunkReview_AllExcludedAndMaximumReasonRemainReviewRequired()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var snapshot = await StoredSnapshotAsync(client);
        var exclusions = snapshot.Projection.GetProperty("chunks").EnumerateArray().Select(chunk =>
            new DocumentChunkExclusionRequest { Ordinal = chunk.GetProperty("ordinal").GetInt32(), Reason = new('x', 500) }).ToArray();
        Assert.NotEmpty(exclusions);
        var result = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!,
            SelectionRequest() with { Exclusions = exclusions }, Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(exclusions.Length, result.Value!.ChunkReview!.Exclusions.Count);
        Assert.All(result.Value.ChunkReview.Exclusions, exclusion => Assert.Equal(500, exclusion.Reason!.Length));
        Assert.False(result.Value.ChunkReview.IndexingApproved);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, result.Value.State);
    }

    [Theory]
    [InlineData("expectedRevision")]
    [InlineData("exclusions")]
    [InlineData("ordinal")]
    public async Task ChunkReview_RequiresExplicitDecisions(string missing)
    {
        using var http = await ClientAsync();
        var snapshot = await StoredSnapshotAsync(new(http, new()));
        var body = new Dictionary<string, object?>
        {
            ["sequencer"] = _source.Sequencer,
            ["eTag"] = _source.ETag,
            ["expectedRevision"] = 0,
            ["exclusions"] = missing == "ordinal" ? new object[] { new { reason = "irrelevant" } } : Array.Empty<object>(),
        };
        if (missing != "ordinal")
            body.Remove(missing);
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.ChunkReview, snapshot.GenerationId), body, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("deleted")]
    [InlineData("replaced")]
    public async Task ChunkReview_FencesOwnershipAndRevokedSources(string change)
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var snapshot = await StoredSnapshotAsync(client);
        Assert.True((await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, SelectionRequest(), Token)).IsSuccess);
        if (change is "deleted" or "replaced")
        {
            await using var db = _database.CreateDbContext();
            var source = await db.KnowledgeDocuments.SingleAsync(Token);
            if (change == "deleted")
                source.Status = DocumentIngestionStates.Deleted;
            else
                source.Sequencer = "2".PadLeft(128, '0');
            await db.SaveChangesAsync(Token);
        }
        var route = DocumentReviewRoutes.Format(DocumentReviewRoutes.ChunkReview,
            change == "app" ? "default" : "tenant-a",
            change == "collection" ? "other" : _source.CollectionId,
            change == "document" ? "other" : _source.DocumentId,
            change == "job" ? "other" : _source.JobId,
            change == "generation" ? "other" : snapshot.GenerationId);
        using var response = await http.PutAsJsonAsync(route, SelectionRequest() with { ExpectedRevision = 1 }, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var readRoute = route[..^"/chunk-review".Length];
        using var read = await http.GetAsync(readRoute, Token);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        await using var retained = _database.CreateDbContext();
        Assert.Single(await retained.DocumentChunkReviews.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ChunkReview_IgnoresForgedApprovalAndActorAndCascadesWithSnapshotRemoval()
    {
        using var http = await ClientAsync();
        var snapshot = await StoredSnapshotAsync(new(http, new()));
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.ChunkReview, snapshot.GenerationId), new
        {
            _source.Sequencer,
            _source.ETag,
            expectedRevision = 0,
            exclusions = Array.Empty<object>(),
            actorId = "forged",
            tenantId = "other",
            indexingApproved = true,
        }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = await response.Content.ReadFromJsonAsync<DocumentReviewGenerationResponse>(Token);
        Assert.False(result!.ChunkReview!.IndexingApproved);
        var json = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("actorId", json, StringComparison.Ordinal);
        await using var db = _database.CreateDbContext();
        Assert.NotEqual("forged", (await db.DocumentChunkReviews.SingleAsync(Token)).ActorId);
        await db.DocumentMarkdownGenerations.ExecuteDeleteAsync(Token);
        Assert.Empty(await db.DocumentChunkReviews.ToArrayAsync(Token));
    }

    private DocumentChunkReviewRequest SelectionRequest() => new()
    {
        Sequencer = _source.Sequencer,
        ETag = _source.ETag!,
        ExpectedRevision = 0,
        Exclusions = [],
    };

    private async Task<DocumentReviewGenerationResponse> StoredSnapshotAsync(AdministrationApiClient client)
    {
        var result = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
#endif
