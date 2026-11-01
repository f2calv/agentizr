#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeApproval_UpgradeRetriesAndCompetingAssessments(bool upgrade)
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new());
        var snapshot = await NativeSelectedSnapshotAsync(client);
        var id = snapshot.GenerationId!;
        await using var db = _database.DbContextFactory.CreateDbContext();
        var original = (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson;
        if (upgrade)
        {
            await db.GetService<IMigrator>().MigrateAsync("20261030054010_DocumentChunkReviews", Token);
            Assert.Single(await db.DocumentChunkReviews.ToArrayAsync(Token));
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Token);
        }
        var request = NativeApprovalRequest();
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.RecordDocumentApprovalAsync(
            "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id, request, Token)));
        Assert.All(writes, result => Assert.True(result.IsSuccess));
        Assert.All(writes, result => Assert.True(result.Value!.Approval!.ReadinessApproved));
        Assert.Single(writes.Select(result => result.Value!.Approval!.RecordedAtUtc).Distinct());
        var row = await db.DocumentApprovals.SingleAsync(Token);
        Assert.False(string.IsNullOrWhiteSpace(row.ActorId));
        Assert.Equal(1, row.ChunkReviewRevision);
        // Native JSONB canonicalization must not break current identical retries.
        Assert.Equal(request, JsonSerializer.Deserialize<DocumentApprovalRequest>(row.AssessmentJson, JsonSerializerOptions.Web));
        var decisions = await Task.WhenAll(new[]
        {
            request with { ExpectedRevision = 1, Notes = "Assessment A" },
            request with { ExpectedRevision = 1, ApprovalRequested = false, Notes = "Withheld assessment B" },
        }.Select(proposal => client.RecordDocumentApprovalAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, proposal, Token)));
        Assert.Single(decisions, result => result.IsSuccess);
        Assert.Single(decisions, result => result.Status == ApiStatus.Conflict);
        var changed = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, id, NativeSelectionRequest() with
            {
                ExpectedRevision = 1,
                Exclusions = [new() { Ordinal = 0 }],
            }, Token);
        Assert.True(changed.IsSuccess);
        Assert.False(changed.Value!.Approval!.IsCurrent);
        Assert.False(changed.Value.Approval.ReadinessApproved);
        Assert.False(changed.Value.Approval.IndexingApproved);
        Assert.Equal(original, (await db.DocumentMarkdownGenerations.AsNoTracking().SingleAsync(Token)).PayloadJson);
        Assert.Equal(2, await db.DocumentApprovals.CountAsync(Token));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(1, _provider.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeApproval_RejectsChangesWhileWaitingForLock(bool revokeSource)
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new());
        var snapshot = await NativeSelectedSnapshotAsync(client);
        await using var db = _database.DbContextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(db, Token);
        var pending = http.PutAsJsonAsync(Path(DocumentReviewRoutes.Approval, snapshot.GenerationId),
            NativeApprovalRequest(), Token);
        await _database.WaitForBlockedAdvisoryLockAsync(Token);
        if (revokeSource)
        {
            var source = await db.KnowledgeDocuments.SingleAsync(Token);
            source.Status = DocumentIngestionStates.Deleted;
        }
        else
            db.DocumentChunkReviews.Add(new()
            {
                TenantId = _source.TenantId,
                GenerationId = snapshot.GenerationId!,
                Revision = 2,
                ActorId = "test-operator",
                ExclusionsJson = "[]",
                RecordedAtUtc = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.DocumentApprovals.ToArrayAsync(Token));
    }

    private DocumentApprovalRequest NativeApprovalRequest() => DocumentApprovalTestData.Request(_source.Sequencer, _source.ETag!);

    private async Task<DocumentReviewGenerationResponse> NativeSelectedSnapshotAsync(AdministrationApiClient client)
    {
        var snapshot = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(snapshot.IsSuccess);
        var selected = await client.RecordDocumentChunkReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, snapshot.Value!.GenerationId!, NativeSelectionRequest(), Token);
        Assert.True(selected.IsSuccess);
        return selected.Value!;
    }
}
#endif
