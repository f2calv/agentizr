#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using CasCap.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Proves the protected management boundary with synthetic receipts and in-process HTTP/SQLite only.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentReviewManagement")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AdministrationDocumentReviewTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly DocumentHybridPreparationTestData.Provider _provider = new();
    private Factory _database = null!;
    private DocumentReviewWebApplicationFactory _host = null!;
    private DocumentIngestionClaim _source = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Token);
        _database = new(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).Options);
        await using var db = _database.CreateDbContext();
        await db.Database.EnsureCreatedAsync(Token);
        _source = await DocumentHybridPreparationTestData.PrepareReviewAsync(_database, _provider, Token);
        _host = new(_database);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TypedClient_PreviewsStoresReadsAndRetriesWithoutPreparation()
    {
        using var http = await ClientAsync();
        using var cacheProbe = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        Assert.True(cacheProbe.Headers.CacheControl?.NoStore);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var preview = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, null, Token);
        Assert.True(preview.IsSuccess);
        Assert.Null(preview.Value!.GenerationId);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, preview.Value.State);
        Assert.Equal(_source.ETag, preview.Value.ETag);
        Assert.Equal("markdown-headings", preview.Value.Projection.GetProperty("profile").GetProperty("strategy").GetString());
        Assert.Equal(["uncertainty-note"], Assert.Single(preview.Value.Pages).Uncertainties);
        await using (var db = _database.CreateDbContext())
            Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));

        var request = Request() with { Review = JsonSerializer.SerializeToElement(DocumentHybridPreparationTestData.SourceReview(), JsonSerializerOptions.Web) };
        var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, request, Token);
        Assert.True(stored.IsSuccess);
        var retry = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, request, Token);
        Assert.Equal(stored.Value!.GenerationId, retry.Value!.GenerationId);
        var read = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, stored.Value.GenerationId, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(stored.Value.Projection.GetRawText(), read.Value!.Projection.GetRawText());
        Assert.Contains("## C", read.Value.Projection.GetProperty("chunks")[0].GetProperty("embeddingText").GetString(), StringComparison.Ordinal);
        var changed = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId,
            request with { TargetTokensPerChunk = 128 }, Token);
        Assert.True(changed.IsSuccess);
        Assert.NotEqual(stored.Value.GenerationId, changed.Value!.GenerationId);
        await using var result = _database.CreateDbContext();
        Assert.Equal(2, await result.DocumentMarkdownGenerations.CountAsync(Token));
        Assert.Empty(await result.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await result.DocumentIngestionJobs.SingleAsync(Token)).Status);
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        var json = JsonSerializer.Serialize(read.Value, JsonSerializerOptions.Web);
        Assert.DoesNotContain("planSha256", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nativeUsage", json, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalogue_ListsCurrentJobAndStoredSnapshotWithoutPrivateInfrastructure()
    {
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var first = await client.GetDocumentsAsync("tenant-a", null, 1, 25, Token);
        Assert.True(first.IsSuccess);
        var item = Assert.Single(first.Value!.Items);
        Assert.Equal(_source.DocumentId, item.DocumentId);
        Assert.Equal(_source.JobId, item.JobId);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, item.JobState);
        Assert.True(item.CanReview);
        Assert.True(item.PreviewConfigured);
        Assert.Null(item.StoredGenerationId);
        var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(stored.IsSuccess);
        var refreshed = await client.GetDocumentsAsync("tenant-a", _source.CollectionId, 1, 25, Token);
        Assert.Equal(stored.Value!.GenerationId, Assert.Single(refreshed.Value!.Items).StoredGenerationId);
        var filtered = await client.GetDocumentsAsync("tenant-a", "another-collection", 1, 25, Token);
        Assert.Empty(filtered.Value!.Items);
        var otherApp = await client.GetDocumentsAsync("default", null, 1, 25, Token);
        Assert.Empty(otherApp.Value!.Items);
        var next = await client.GetDocumentsAsync("tenant-a", null, 2, 1, Token);
        Assert.Empty(next.Value!.Items);
        Assert.Equal(1, next.Value.TotalCount);
        using var response = await http.GetAsync(
            AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, "tenant-a"), Token);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("https://", json, StringComparison.Ordinal);
        Assert.DoesNotContain("preparationPlan", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("superseded")]
    [InlineData("claimed")]
    [InlineData("unfinished")]
    public async Task Catalogue_ObservesReviewRevocation(string change)
    {
        await using (var db = _database.CreateDbContext())
        {
            var document = await db.KnowledgeDocuments.SingleAsync(Token);
            var job = await db.DocumentIngestionJobs.SingleAsync(Token);
            if (change == "deleted")
                document.Status = DocumentIngestionStates.Deleted;
            if (change == "superseded")
                document.Sequencer = "2".PadLeft(128, '0');
            if (change == "claimed")
                job.ClaimId = Guid.NewGuid();
            if (change == "unfinished")
                job.FinishedAtUtc = null;
            await db.SaveChangesAsync(Token);
        }
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var page = await client.GetDocumentsAsync("tenant-a", null, 1, 25, Token);
        Assert.True(page.IsSuccess);
        var item = Assert.Single(page.Value!.Items);
        Assert.False(item.CanReview);
        if (change == "superseded")
            Assert.Null(item.JobId);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=10001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Catalogue_RejectsInvalidBounds(string query)
    {
        using var http = await ClientAsync();
        using var response = await http.GetAsync(
            AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, "tenant-a") + "?" + query, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("bearer", HttpStatusCode.Unauthorized)]
    [InlineData("other-admin", HttpStatusCode.Forbidden)]
    public async Task Authorization_PrecedesReadsAndWrites(string caller, HttpStatusCode status)
    {
        using var http = _host.CreateClient();
        if (caller == "bearer")
            http.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        if (caller == "other-admin")
            http.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader,
                AdministrationSignInWebApplicationFactory.OtherAppAdministrator);
        using var preview = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var catalogue = await http.GetAsync(AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, "tenant-a"), Token);
        using var read = await http.GetAsync(Path(DocumentReviewRoutes.Generation, "missing"), Token);
        using var write = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(status, preview.StatusCode);
        Assert.Equal(status, catalogue.StatusCode);
        Assert.Equal(status, read.StatusCode);
        Assert.Equal(status, write.StatusCode);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Antiforgery_RejectsMissingAndInvalidTokens(bool invalid)
    {
        using var http = _host.CreateClient();
        http.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader,
            AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        if (invalid)
            http.DefaultRequestHeaders.Add(AdministrationRoutes.AntiforgeryHeaderName, "invalid");
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    public async Task Ownership_FencesAuthorizedRequests(string field)
    {
        using var http = await ClientAsync();
        var path = DocumentReviewRoutes.Format(DocumentReviewRoutes.Source,
            field == "app" ? "default" : "tenant-a", field == "collection" ? "other" : _source.CollectionId,
            field == "document" ? "other" : _source.DocumentId, field == "job" ? "other" : _source.JobId);
        using var response = await http.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var write = await http.PostAsJsonAsync(path + "/generations", Request(), Token);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Theory]
    [InlineData("etag", HttpStatusCode.Conflict)]
    [InlineData("sequence", HttpStatusCode.Conflict)]
    [InlineData("budget", HttpStatusCode.BadRequest)]
    [InlineData("shape", HttpStatusCode.BadRequest)]
    [InlineData("digest", HttpStatusCode.BadRequest)]
    public async Task InvalidDecisions_DoNotPersist(string field, HttpStatusCode status)
    {
        using var http = await ClientAsync();
        var request = Request();
        request = field switch
        {
            "etag" => request with { ETag = "stale" },
            "sequence" => request with { Sequencer = "stale" },
            "budget" => request with { MaximumTokensPerChunk = 8, OverlapTokens = 8 },
            "shape" => request with { Review = JsonSerializer.SerializeToElement(new { approved = true, actor = "forged" }) },
            "digest" => request with
            {
                Review = JsonSerializer.SerializeToElement(
                    DocumentHybridPreparationTestData.SourceReview() with { MarkdownSha256 = new('0', 64) }, JsonSerializerOptions.Web),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), request, Token);
        Assert.Equal(status, response.StatusCode);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLoss_RevokesRetainedGeneration(bool deleted)
    {
        using var http = await ClientAsync();
        using var created = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stored = await created.Content.ReadFromJsonAsync<DocumentReviewGenerationResponse>(Token);
        Assert.Equal(Path(DocumentReviewRoutes.Generation, stored!.GenerationId), created.Headers.Location!.OriginalString);
        await using (var db = _database.CreateDbContext())
        {
            var source = await db.KnowledgeDocuments.SingleAsync(Token);
            if (deleted)
                source.Status = DocumentIngestionStates.Deleted;
            else
                source.Sequencer = "2".PadLeft(128, '0');
            await db.SaveChangesAsync(Token);
        }
        using var read = await http.GetAsync(created.Headers.Location, Token);
        using var preview = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var write = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        await using var retained = _database.CreateDbContext();
        Assert.Single(await retained.DocumentMarkdownGenerations.ToArrayAsync(Token));
    }

    [Fact]
    public async Task CorruptReceipts_AreServerErrorsNotInputValidation()
    {
        await using (var db = _database.CreateDbContext())
        {
            var receipt = await db.DocumentPreparationOperations.FirstAsync(Token);
            receipt.ResultJson = "{}";
            await db.SaveChangesAsync(Token);
        }
        using var http = await ClientAsync();
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("claimed")]
    [InlineData("unfinished")]
    [InlineData("no-plan")]
    public async Task ReviewRequiresFinishedReleasedPlannedJob(string field)
    {
        await using (var db = _database.CreateDbContext())
        {
            var job = await db.DocumentIngestionJobs.SingleAsync(Token);
            if (field == "processing")
                job.Status = DocumentIngestionStates.Processing;
            if (field == "claimed")
                job.ClaimId = Guid.NewGuid();
            if (field == "unfinished")
                job.FinishedAtUtc = null;
            if (field == "no-plan")
                job.PreparationPlanJson = null;
            await db.SaveChangesAsync(Token);
        }
        using var http = await ClientAsync();
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var catalogue = await client.GetDocumentsAsync("tenant-a", null, 1, 25, Token);
        Assert.Equal(field != "no-plan", Assert.Single(catalogue.Value!.Items).PreviewConfigured);
        using var preview = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var write = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task RequestCannotSupplyTenantActorSourceLocationOrApproval()
    {
        using var http = await ClientAsync();
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), new
        {
            _source.Sequencer,
            _source.ETag,
            tenantId = "other",
            actor = "forged",
            blobUri = "https://evil.invalid/other.pdf",
            state = "Approved",
            claimId = Guid.NewGuid(),
        }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await response.Content.ReadFromJsonAsync<DocumentReviewGenerationResponse>(Token);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, stored!.State);
        await using var db = _database.CreateDbContext();
        Assert.Equal("tenant-a", (await db.DocumentMarkdownGenerations.SingleAsync(Token)).TenantId);
        Assert.Equal(_source.BlobUri, (await db.KnowledgeDocuments.SingleAsync(Token)).BlobUri);
    }

    [Fact]
    public async Task MissingStorage_IsExplicitlyUnsupported()
    {
        await using var host = new DocumentReviewWebApplicationFactory(null);
        using var http = host.CreateClient();
        await AdministrationTestClient.PrepareAsync(http, AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        using var preview = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var catalogue = await http.GetAsync(AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, "tenant-a"), Token);
        using var write = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.NotImplemented, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, catalogue.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, write.StatusCode);
    }

    [Fact]
    public void SourceLookup_TranslatesOnNpgsqlWithoutConnecting()
    {
        using var db = new AgentRuntimeDbContext(new DbContextOptionsBuilder<AgentRuntimeDbContext>()
            .UseNpgsql("Host=invalid.example;Database=design;Username=design").Options);
        var sql = DocumentReviewAdministrationService.SourceQuery(db, "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId).ToQueryString();
        Assert.Contains("JOIN agent_runtime.knowledge_documents", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenDevelopment_DoesNotAuthorizeReviewManagement()
    {
        await using var host = new AgentDefinitionAdministrationWebApplicationFactory();
        using var http = host.CreateClient();
        using var response = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var catalogue = await http.GetAsync(AdministrationRoutes.Format(DocumentCatalogueRoutes.Documents, "tenant-a"), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, catalogue.StatusCode);
    }

    private DocumentReviewGenerationRequest Request() => new() { Sequencer = _source.Sequencer, ETag = _source.ETag! };

    private string Path(string route, string? id = null) =>
        DocumentReviewRoutes.Format(route, "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id);

    private async Task<HttpClient> ClientAsync()
    {
        var http = _host.CreateClient();
        await AdministrationTestClient.PrepareAsync(http, AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        return http;
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }

}
#endif
