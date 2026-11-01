#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CasCap.IntegrationTests;

public sealed partial class AdministrationDocumentReviewTests
{
    [Fact]
    public async Task Approval_LifecycleInvalidatesOnSelectionChangeWithoutIndexing()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var request = ApprovalRequest();
        var first = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.True(first.IsSuccess);
        var approval = first.Value!.Approval!;
        Assert.True(approval.IsCurrent);
        Assert.True(approval.ReadinessApproved);
        Assert.False(approval.IndexingApproved);
        Assert.Equal(1, approval.Revision);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, first.Value.State);
        var retry = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.Equal(approval.RecordedAtUtc, retry.Value!.Approval!.RecordedAtUtc);
        var changed = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, SelectionRequest() with
            {
                ExpectedRevision = 1,
                Exclusions = [new() { Ordinal = 0 }],
            }, Token);
        Assert.True(changed.IsSuccess);
        Assert.False(changed.Value!.Approval!.IsCurrent);
        Assert.False(changed.Value.Approval.ReadinessApproved);
        var read = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId, Token);
        Assert.False(read.Value!.Approval!.IsCurrent);
        var stale = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.Equal(ApiStatus.Conflict, stale.Status);
        var withheld = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request with
            {
                ChunkReviewRevision = 2,
                ExpectedRevision = 1,
                ApprovalRequested = false,
                Notes = "<script>Reassessment withheld</script>",
            }, Token);
        Assert.True(withheld.IsSuccess);
        Assert.False(withheld.Value!.Approval!.ReadinessApproved);
        Assert.Equal(2, withheld.Value.Approval.Revision);
        await using var db = _database.CreateDbContext();
        var records = await db.DocumentApprovals.OrderBy(item => item.Revision).ToArrayAsync(Token);
        Assert.Equal(2, records.Length);
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.ActorId)));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(snapshot.Projection.GetRawText(), withheld.Value.Projection.GetRawText());
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        await db.DocumentMarkdownGenerations.ExecuteDeleteAsync(Token);
        Assert.Empty(await db.DocumentApprovals.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("manual", "figure/caption")]
    [InlineData("cefr", "CEFR")]
    [InlineData("provenance", "provenance")]
    [InlineData("content", "uncertainties")]
    [InlineData("safety", "Safety")]
    [InlineData("withheld", "withheld")]
    [InlineData("all-excluded", "included candidate")]
    public async Task Approval_UnresolvedGatesRemainBlocked(string gate, string text)
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client, gate == "all-excluded");
        var request = gate switch
        {
            "manual" => ApprovalRequest() with { DocumentType = DocumentApprovalValues.DeviceManual, CefrLevel = null },
            "cefr" => ApprovalRequest() with { CefrLevel = null },
            "provenance" => ApprovalRequest() with { ProvenanceVerified = false },
            "content" => ApprovalRequest() with { ContentVerified = false },
            "safety" => ApprovalRequest() with { SafetyVerified = false },
            "withheld" => ApprovalRequest() with { ApprovalRequested = false },
            _ => ApprovalRequest(),
        };
        var result = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, request, Token);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approval!.ReadinessApproved);
        Assert.Contains(result.Value.Approval.Blockers, blocker => blocker.Contains(text, StringComparison.Ordinal));
        Assert.False(result.Value.Approval.IndexingApproved);
    }

    [Theory]
    [InlineData("type", HttpStatusCode.BadRequest)]
    [InlineData("language", HttpStatusCode.BadRequest)]
    [InlineData("level", HttpStatusCode.BadRequest)]
    [InlineData("notes", HttpStatusCode.BadRequest)]
    [InlineData("long-notes", HttpStatusCode.BadRequest)]
    [InlineData("edition", HttpStatusCode.BadRequest)]
    [InlineData("negative", HttpStatusCode.BadRequest)]
    [InlineData("overflow", HttpStatusCode.BadRequest)]
    [InlineData("selection-overflow", HttpStatusCode.BadRequest)]
    [InlineData("unsaved-selection", HttpStatusCode.BadRequest)]
    [InlineData("stale-selection", HttpStatusCode.Conflict)]
    [InlineData("stale-assessment", HttpStatusCode.Conflict)]
    [InlineData("etag", HttpStatusCode.Conflict)]
    public async Task Approval_RejectsInvalidOrStaleRequests(string change, HttpStatusCode expected)
    {
        using var http = await ClientAsync();
        var snapshot = await SelectedSnapshotAsync(new(http, new()));
        var request = change switch
        {
            "type" => ApprovalRequest() with { DocumentType = "Other" },
            "language" => ApprovalRequest() with { Language = "invalid" },
            "level" => ApprovalRequest() with { CefrLevel = "A0" },
            "notes" => ApprovalRequest() with { Notes = "  " },
            "long-notes" => ApprovalRequest() with { Notes = new('x', 2001) },
            "edition" => ApprovalRequest() with { Edition = new('x', 201) },
            "negative" => ApprovalRequest() with { ExpectedRevision = -1 },
            "overflow" => ApprovalRequest() with { ExpectedRevision = long.MaxValue },
            "selection-overflow" => ApprovalRequest() with { ChunkReviewRevision = long.MaxValue },
            "unsaved-selection" => ApprovalRequest() with { ChunkReviewRevision = 0 },
            "stale-selection" => ApprovalRequest() with { ChunkReviewRevision = 2 },
            "stale-assessment" => ApprovalRequest() with { ExpectedRevision = 1 },
            "etag" => ApprovalRequest() with { ETag = "stale" },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.Approval, snapshot.GenerationId), request, Token);
        Assert.Equal(expected, response.StatusCode);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentApprovals.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("approvalRequested")]
    [InlineData("provenanceVerified")]
    [InlineData("contentVerified")]
    [InlineData("safetyVerified")]
    [InlineData("chunkReviewRevision")]
    [InlineData("expectedRevision")]
    public async Task Approval_RequiresExplicitDecisions(string missing)
    {
        using var http = await ClientAsync();
        var snapshot = await SelectedSnapshotAsync(new(http, new()));
        var json = JsonSerializer.SerializeToNode(ApprovalRequest(), JsonSerializerOptions.Web)!.AsObject();
        Assert.True(json.Remove(missing));
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.Approval, snapshot.GenerationId), json, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("collection")]
    [InlineData("generation")]
    [InlineData("deleted")]
    [InlineData("replaced")]
    public async Task Approval_FencesOwnershipAndRevocation(string change)
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var saved = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, ApprovalRequest(), Token);
        Assert.True(saved.IsSuccess);
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
        var route = DocumentReviewRoutes.Format(DocumentReviewRoutes.Approval,
            change == "app" ? "default" : "tenant-a",
            change == "collection" ? "other" : _source.CollectionId, _source.DocumentId, _source.JobId,
            change == "generation" ? "other" : snapshot.GenerationId);
        using var response = await http.PutAsJsonAsync(route, ApprovalRequest() with { ExpectedRevision = 1 }, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var read = await http.GetAsync(route[..^"/approval".Length], Token);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Approval_RequiresSavedSelectionAndIgnoresForgedReadiness()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await StoredSnapshotAsync(client);
        var missing = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, ApprovalRequest(), Token);
        Assert.Equal(ApiStatus.Conflict, missing.Status);
        var selected = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, SelectionRequest(), Token);
        Assert.True(selected.IsSuccess);
        var json = JsonSerializer.SerializeToNode(ApprovalRequest() with { ContentVerified = false }, JsonSerializerOptions.Web)!.AsObject();
        json["actorId"] = "forged-actor";
        json["readinessApproved"] = true;
        json["indexingApproved"] = true;
        json["blockers"] = new JsonArray();
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.Approval, snapshot.GenerationId), json, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<DocumentReviewGenerationResponse>(Token);
        Assert.False(saved!.Approval!.ReadinessApproved);
        Assert.False(saved.Approval.IndexingApproved);
        await using var db = _database.CreateDbContext();
        Assert.NotEqual("forged-actor", (await db.DocumentApprovals.SingleAsync(Token)).ActorId);
    }

    [Fact]
    public async Task Approval_ValidationRetainsFieldErrorsInTypedClient()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new());
        var snapshot = await SelectedSnapshotAsync(client);
        var result = await client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, ApprovalRequest() with { Notes = string.Empty }, Token);
        Assert.Equal(ApiStatus.ValidationFailed, result.Status);
        Assert.NotEmpty(result.Errors!);
        Assert.Contains(result.FieldErrors!, pair => pair.Key.EndsWith("Notes", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("[{\"ordinal\":999}]")]
    [InlineData("[{\"ordinal\":0},{\"ordinal\":0}]")]
    [InlineData("[null]")]
    public async Task Approval_RejectsCorruptSavedSelectionBeforeInsertion(string exclusions)
    {
        using var http = await ClientAsync();
        var snapshot = await SelectedSnapshotAsync(new(http, new()));
        await using var db = _database.CreateDbContext();
        (await db.DocumentChunkReviews.SingleAsync(Token)).ExclusionsJson = exclusions;
        await db.SaveChangesAsync(Token);
        using var response = await http.PutAsJsonAsync(Path(DocumentReviewRoutes.Approval, snapshot.GenerationId),
            ApprovalRequest(), Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await db.DocumentApprovals.ToArrayAsync(Token));
    }

    private DocumentApprovalRequest ApprovalRequest() => DocumentApprovalTestData.Request(_source.Sequencer, _source.ETag!);

    private async Task<DocumentReviewGenerationResponse> SelectedSnapshotAsync(AdministrationApiClient client, bool excludeAll = false)
    {
        var snapshot = await StoredSnapshotAsync(client);
        var selection = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.GenerationId!, SelectionRequest() with
            {
                Exclusions = excludeAll ? snapshot.Projection.GetProperty("chunks").EnumerateArray()
                    .Select(chunk => new DocumentChunkExclusionRequest { Ordinal = chunk.GetProperty("ordinal").GetInt32() }).ToArray() : [],
            }, Token);
        Assert.True(selection.IsSuccess);
        return selection.Value!;
    }
}
#endif
