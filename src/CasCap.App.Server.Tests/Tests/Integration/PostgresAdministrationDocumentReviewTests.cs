#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using CasCap.Tests;
using CasCap.Tests.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Exercises protected HTTP management against native JSONB plans and isolated PostgreSQL transactions.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "PostgreSQL")]
[Trait("Category", "DocumentReviewManagement")]
[Collection(HostIntegrationCollection.Name)]
public sealed partial class PostgresAdministrationDocumentReviewTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _database = new();
    private readonly DocumentHybridPreparationTestData.Provider _provider = new();
    private DocumentReviewWebApplicationFactory _host = null!;
    private DocumentIngestionClaim _source = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _source = await DocumentHybridPreparationTestData.PrepareReviewAsync(_database.DbContextFactory, _provider, Token);
        _host = new(_database.DbContextFactory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task NativePlanPreviewAndConcurrentRetriesPreserveReceipts()
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var preview = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, null, Token);
        Assert.True(preview.IsSuccess);
        Assert.Null(preview.Value!.GenerationId);
        Assert.Equal(_source.Sequencer, preview.Value.Sequencer);
        Assert.Equal(_source.ETag, preview.Value.ETag);
        Assert.Equal(DocumentHybridPreparationTestData.ReviewMarkdown, Assert.Single(preview.Value.Pages).Markdown);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Empty(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
        var receipts = await ReceiptJsonAsync(db);
        var planType = await db.Database.SqlQuery<string>($"""
            SELECT udt_name AS "Value" FROM information_schema.columns WHERE table_schema = 'agent_runtime'
                AND table_name = 'document_ingestion_jobs' AND column_name = 'preparation_plan_json'
            """).SingleAsync(Token);
        Assert.Equal("jsonb", planType);
        Assert.Equal(_source.PreparationPlanJson, (await db.DocumentIngestionJobs.AsNoTracking().SingleAsync(Token)).PreparationPlanJson);
        Assert.True((await client.GetSessionAsync(Token)).IsSuccess);
        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.StoreDocumentReviewAsync(
            "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, Request(), Token)));
        Assert.All(writes, write => Assert.True(write.IsSuccess));
        var id = Assert.Single(writes.Select(write => write.Value!.GenerationId).Distinct());
        Assert.NotNull(id);
        var read = await client.GetDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, read.Value!.State);
        Assert.Equal(2, read.Value.Projection.GetProperty("review").GetProperty("version").GetInt32());
        Assert.Contains("## C", read.Value.Projection.GetProperty("chunks")[0].GetProperty("embeddingText").GetString(), StringComparison.Ordinal);
        var changed = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId, _source.DocumentId,
            _source.JobId, Request() with { TargetTokensPerChunk = 128 }, Token);
        Assert.True(changed.IsSuccess);
        Assert.NotEqual(id, changed.Value!.GenerationId);
        Assert.Equal(2, await db.DocumentMarkdownGenerations.CountAsync(Token));
        Assert.Equal(receipts, await ReceiptJsonAsync(db));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationGenerations.ToArrayAsync(Token));
        Assert.Equal(DocumentIngestionStates.ReviewRequired, (await db.DocumentIngestionJobs.AsNoTracking().SingleAsync(Token)).Status);
        Assert.Equal(1, _provider.Submissions);
        Assert.Equal(1, _provider.ModelCalls);
        using var response = await http.GetAsync(Path(DocumentReviewRoutes.Generation, id), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("planSha256", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nativeUsage", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeCatalogue_MaterializesCurrentSourceAndGeneration()
    {
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var page = await client.GetDocumentsAsync("tenant-a", _source.CollectionId, 1, 1, Token);
        Assert.True(page.IsSuccess);
        var item = Assert.Single(page.Value!.Items);
        Assert.Equal(_source.DocumentId, item.DocumentId);
        Assert.Equal(_source.JobId, item.JobId);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, item.JobState);
        Assert.Equal(Uri.UnescapeDataString(new Uri(_source.BlobUri).Segments[^1]), item.FileName);
        Assert.True(item.CanReview);
        Assert.Null(item.StoredGenerationId);
        var stored = await client.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(stored.IsSuccess);
        var refreshed = await client.GetDocumentsAsync("tenant-a", null, 1, 25, Token);
        Assert.Equal(stored.Value!.GenerationId, Assert.Single(refreshed.Value!.Items).StoredGenerationId);
        var another = await client.GetDocumentsAsync("default", null, 1, 25, Token);
        Assert.True(another.IsSuccess);
        Assert.Empty(another.Value!.Items);
        await using var db = _database.DbContextFactory.CreateDbContext();
        var document = await db.KnowledgeDocuments.SingleAsync(Token);
        document.Sequencer = "2".PadLeft(128, '0');
        await db.SaveChangesAsync(Token);
        var superseded = await client.GetDocumentsAsync("tenant-a", null, 1, 25, Token);
        var current = Assert.Single(superseded.Value!.Items);
        Assert.False(current.CanReview);
        Assert.Null(current.JobId);
        Assert.Null(current.StoredGenerationId);
        Assert.Equal(1, _provider.ModelCalls);
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
    }

    [Fact]
    public async Task RealCookieAndAntiforgerySurviveReviewReplicaChanges()
    {
        await using var first = _host.WithWebHostBuilder(ConfigureCookieReplica);
        await using var second = _host.WithWebHostBuilder(ConfigureCookieReplica);
        var address = new Uri("https://localhost");
        var cookies = new CookieContainer();
        var options = first.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AdministrationAuthenticationSchemes.Cookie);
        var identity = new ClaimsIdentity(
            [
                new Claim("oid", AdministrationSignInWebApplicationFactory.DefaultAppAdministrator),
                new Claim("sub", "synthetic-review-administrator"),
            ],
            AdministrationAuthenticationSchemes.Cookie);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        }, AdministrationAuthenticationSchemes.Cookie);
        cookies.Add(address, new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket)));
        using var firstHttp = first.CreateDefaultClient(address, new CookieContainerHandler(cookies));
        using var secondHttp = second.CreateDefaultClient(address, new CookieContainerHandler(cookies));
        var session = await AdministrationTestClient.PrepareAsync(firstHttp);
        Assert.True(session.SignInRequired);
        Assert.True(session.IsAuthenticated);
        secondHttp.DefaultRequestHeaders.Add(AdministrationRoutes.AntiforgeryHeaderName, session.AntiforgeryToken);
        var firstClient = new AdministrationApiClient(firstHttp, new AdministrationSessionState());
        var secondClient = new AdministrationApiClient(secondHttp, new AdministrationSessionState());
        var preview = await firstClient.GetDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, null, Token);
        Assert.True(preview.IsSuccess);
        var stored = await secondClient.StoreDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, Request(), Token);
        Assert.True(stored.IsSuccess);
        var read = await firstClient.GetDocumentReviewAsync("tenant-a", _source.CollectionId,
            _source.DocumentId, _source.JobId, stored.Value!.GenerationId, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(stored.Value.GenerationId, read.Value!.GenerationId);
        Assert.Equal(DocumentIngestionStates.ReviewRequired, read.Value.State);
        using var anonymous = second.CreateClient(new() { BaseAddress = address });
        using var denied = await anonymous.GetAsync(Path(DocumentReviewRoutes.Generation, stored.Value.GenerationId), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.NotEmpty(await db.DataProtectionKeys.ToArrayAsync(Token));
        Assert.Single(await db.DocumentMarkdownGenerations.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentTextChunks.ToArrayAsync(Token));
        Assert.NotSame(first.Services.GetRequiredService<IDataProtectionProvider>(),
            second.Services.GetRequiredService<IDataProtectionProvider>());
    }

    [Theory]
    [InlineData("app")]
    [InlineData("collection")]
    [InlineData("document")]
    [InlineData("job")]
    public async Task NativeSourceLookupKeepsOwnershipQualified(string field)
    {
        using var http = await ClientAsync(_host);
        var path = DocumentReviewRoutes.Format(DocumentReviewRoutes.Source,
            field == "app" ? "default" : "tenant-a", field == "collection" ? "other" : _source.CollectionId,
            field == "document" ? "other" : _source.DocumentId, field == "job" ? "other" : _source.JobId);
        using var preview = await http.GetAsync(path, Token);
        using var write = await http.PostAsJsonAsync(path + "/generations", Request(), Token);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        await AssertCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLossBetweenPreviewStorageAndReadRevokesAccess(bool deleted)
    {
        using var http = await ClientAsync(_host);
        using var preview = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var created = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stored = await created.Content.ReadFromJsonAsync<DocumentReviewGenerationResponse>(Token);
        Assert.Equal(Path(DocumentReviewRoutes.Generation, stored!.GenerationId), created.Headers.Location!.OriginalString);
        await using (var db = _database.DbContextFactory.CreateDbContext())
        {
            var source = await db.KnowledgeDocuments.SingleAsync(Token);
            if (deleted)
                source.Status = DocumentIngestionStates.Deleted;
            else
                source.Sequencer = "2".PadLeft(128, '0');
            await db.SaveChangesAsync(Token);
        }
        using var read = await http.GetAsync(created.Headers.Location, Token);
        using var unavailable = await http.GetAsync(Path(DocumentReviewRoutes.Source), Token);
        using var write = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        await AssertCountAsync(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLossWhileHttpWriteWaitsReturnsConflict(bool deleted)
    {
        using var http = await ClientAsync(_host);
        await using var owner = _database.DbContextFactory.CreateDbContext();
        await using var held = await owner.Database.BeginTransactionAsync(Token);
        await DocumentIngestionTransactions.LockAsync(owner, Token);
        Task<HttpResponseMessage>? pending = null;
        HttpResponseMessage? response = null;
        var released = false;
        try
        {
            pending = http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
            await _database.WaitForBlockedAdvisoryLockAsync(Token);
            Assert.False(pending.IsCompleted);
            await AssertCountAsync(0);
            var source = await owner.KnowledgeDocuments.SingleAsync(Token);
            if (deleted)
                source.Status = DocumentIngestionStates.Deleted;
            else
                source.Sequencer = "2".PadLeft(128, '0');
            await owner.SaveChangesAsync(Token);
            await held.CommitAsync(Token);
            released = true;
            response = await pending;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertCountAsync(0);
        }
        finally
        {
            if (!released)
                await held.RollbackAsync(CancellationToken.None);
            if (pending is not null)
                response ??= await pending;
            response?.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrSourceLossAfterInsertRollsBackBeforeHttpOutcome(bool sourceLoss)
    {
        var checkpoint = new SaveCheckpoint(async (db, token) =>
        {
            Assert.Equal(1, await db.DocumentMarkdownGenerations.CountAsync(token));
            await AssertCountAsync(0);
            if (!sourceLoss)
                throw new IOException("Synthetic post-insert failure.");
            await db.KnowledgeDocuments.ExecuteUpdateAsync(update => update.SetProperty(item => item.Sequencer, "2"), token);
        });
        await using var host = new DocumentReviewWebApplicationFactory(_database.CreateDbContextFactory(checkpoint));
        using var http = await ClientAsync(host);
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(sourceLoss ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertCountAsync(0);
        await using (var db = _database.DbContextFactory.CreateDbContext())
            Assert.Equal(_source.Sequencer, (await db.KnowledgeDocuments.SingleAsync(Token)).Sequencer);
        using var retry = await ClientAsync(_host);
        using var created = await retry.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertCountAsync(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptJsonbReceiptsAreServerErrors(bool missing)
    {
        await using (var db = _database.DbContextFactory.CreateDbContext())
        {
            var receipt = await db.DocumentPreparationOperations.SingleAsync(
                item => item.Stage == DocumentPreparationOperations.PreparationAgent, Token);
            receipt.ResultJson = missing ? null : "{}";
            await db.SaveChangesAsync(Token);
        }
        using var http = await ClientAsync(_host);
        using var response = await http.PostAsJsonAsync(Path(DocumentReviewRoutes.Generations), Request(), Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertCountAsync(0);
    }

    private DocumentReviewGenerationRequest Request() => new()
    {
        Sequencer = _source.Sequencer,
        ETag = _source.ETag!,
        Review = JsonSerializer.SerializeToElement(DocumentHybridPreparationTestData.SourceReview(), JsonSerializerOptions.Web),
    };

    private string Path(string route, string? id = null) =>
        DocumentReviewRoutes.Format(route, "tenant-a", _source.CollectionId, _source.DocumentId, _source.JobId, id);

    private static async Task<HttpClient> ClientAsync(DocumentReviewWebApplicationFactory host)
    {
        var http = host.CreateClient();
        await AdministrationTestClient.PrepareAsync(http, AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        return http;
    }

    private void ConfigureCookieReplica(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AgentRuntimeDbContext>();
            services.AddScoped(_ => _database.DbContextFactory.CreateDbContext());
            services.AddDataProtection().PersistKeysToDbContext<AgentRuntimeDbContext>();
            services.Configure<AuthenticationOptions>(options =>
                options.Schemes.Single(scheme => scheme.Name == AdministrationAuthenticationSchemes.Cookie)
                    .HandlerType = typeof(CookieAuthenticationHandler));
        });

    private async Task AssertCountAsync(int expected)
    {
        await using var db = _database.DbContextFactory.CreateDbContext();
        Assert.Equal(expected, await db.DocumentMarkdownGenerations.CountAsync(Token));
    }

    private static async Task<string> ReceiptJsonAsync(AgentRuntimeDbContext db) =>
        JsonSerializer.Serialize(await db.DocumentPreparationOperations.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync(Token));

    private sealed class SaveCheckpoint(Func<AgentRuntimeDbContext, CancellationToken, Task> saved) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            await saved(Assert.IsType<AgentRuntimeDbContext>(eventData.Context), cancellationToken);
            return result;
        }
    }
}
#endif
