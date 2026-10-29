#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Client.Services;
using CasCap.Constants;
using CasCap.Entities;
using CasCap.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Exercises cookie-authorized, antiforgery-protected durable requests on an API-only replica without Blob/queue clients.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "DocumentSynchronizationManagement")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AdministrationDocumentSynchronizationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private Factory _database = null!;
    private DocumentReviewWebApplicationFactory _host = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DocumentIngestionConfig Config => DocumentIngestionTestData.Config with
    {
        SynchronizationEnabled = true,
        Sources =
        [
            DocumentIngestionTestData.Config.Sources[0],
            DocumentIngestionTestData.Config.Sources[0] with
            {
                ContainerUri = "https://storage.example.com/exams",
                CollectionId = "exams",
            },
            DocumentIngestionTestData.Config.Sources[0] with
            {
                ContainerUri = "https://storage.example.com/another",
                TenantId = "tenant-b",
                CollectionId = "another",
            },
        ],
    };

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Token);
        _database = new(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).Options);
        await using var db = _database.CreateDbContext();
        await db.Database.EnsureCreatedAsync(Token);
        _host = new(_database, ["tenant-a", "tenant-b"], Config);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ReadDoesNotRequestWorkAndHidesSourceCoordinates()
    {
        using var http = await ClientAsync(_host);
        using var response = await http.GetAsync(Path("tenant-a"), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = await response.Content.ReadFromJsonAsync<DocumentSynchronizationResponse>(Token);
        Assert.True(result!.Enabled);
        Assert.False(result.ProcessingEnabled);
        Assert.Equal("0 * * * *", result.Cron);
        Assert.Equal(["exams", "manuals"], result.Sources.Select(item => item.CollectionId));
        Assert.All(result.Sources, item =>
        {
            Assert.Equal("NotRun", item.State);
            Assert.Null(item.RequestedAtUtc);
            Assert.Null(item.LastSucceededAtUtc);
        });
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        Assert.DoesNotContain("https://", json, StringComparison.Ordinal);
        Assert.DoesNotContain("containerUri", json, StringComparison.Ordinal);
        Assert.DoesNotContain("topic", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", json, StringComparison.Ordinal);
        await AssertNoWorkAsync();
    }

    [Theory]
    [InlineData("tenant-a", 2)]
    [InlineData("tenant-b", 1)]
    public async Task AcceptedRequestIsAppScopedAndRepeatSafe(string app, int expected)
    {
        using var http = await ClientAsync(_host);
        using var response = await http.PostAsJsonAsync(Path(app), new
        {
            tenantId = app == "tenant-a" ? "tenant-b" : "tenant-a",
            collectionId = "forged",
            blobUri = "https://evil.invalid/forged.pdf",
        }, Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(Path(app), response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var accepted = await response.Content.ReadFromJsonAsync<DocumentSynchronizationResponse>(Token);
        Assert.Equal(expected, accepted!.Sources.Count);
        Assert.All(accepted.Sources, source => Assert.Equal(DocumentSynchronizationStates.Queued, source.State));
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var repeated = await client.RequestDocumentSynchronizationAsync(app, Token);
        Assert.True(repeated.IsSuccess);
        var read = await client.GetDocumentSynchronizationAsync(app, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(expected, read.Value!.Sources.Count);
        await using var db = _database.CreateDbContext();
        var requests = await db.DocumentSynchronizations.ToArrayAsync(Token);
        Assert.Equal(expected, requests.Length);
        Assert.All(requests, source =>
        {
            Assert.Equal(app, source.TenantId);
            Assert.True(source.Requested);
            Assert.Null(source.RunId);
        });
        Assert.Empty(await db.KnowledgeDocuments.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentIngestionJobs.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentPreparationOperations.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("bearer", HttpStatusCode.Unauthorized)]
    [InlineData("other-admin", HttpStatusCode.Forbidden)]
    public async Task AuthorizationPrecedesReadAndRequest(string caller, HttpStatusCode expected)
    {
        using var http = _host.CreateClient();
        if (caller == "bearer")
            http.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        if (caller == "other-admin")
            http.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader,
                AdministrationSignInWebApplicationFactory.OtherAppAdministrator);
        using var read = await http.GetAsync(Path("tenant-a"), Token);
        using var write = await http.PostAsJsonAsync(Path("tenant-a"), new { }, Token);
        Assert.Equal(expected, read.StatusCode);
        Assert.Equal(expected, write.StatusCode);
        await AssertNoWorkAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AntiforgeryRejectsMissingAndInvalidTokens(bool invalid)
    {
        using var http = _host.CreateClient();
        http.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader,
            AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        if (invalid)
            http.DefaultRequestHeaders.Add(AdministrationRoutes.AntiforgeryHeaderName, "invalid");
        using var response = await http.PostAsJsonAsync(Path("tenant-a"), new { }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoWorkAsync();
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("disabled")]
    [InlineData("unmapped")]
    public async Task UnavailableOperationNeverAcceptsWork(string unavailable)
    {
        var config = unavailable == "disabled" ? Config with { SynchronizationEnabled = false } : Config;
        if (unavailable == "unmapped")
            config = config with { Sources = [Config.Sources[^1]] };
        await using var host = new DocumentReviewWebApplicationFactory(
            unavailable == "storage" ? null : _database, ["tenant-a", "tenant-b"], config);
        using var http = await ClientAsync(host);
        using var read = await http.GetAsync(Path("tenant-a"), Token);
        Assert.Equal(unavailable == "storage" ? HttpStatusCode.NotImplemented : HttpStatusCode.OK, read.StatusCode);
        using var response = await http.PostAsJsonAsync(Path("tenant-a"), new { }, Token);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        await AssertNoWorkAsync();
    }

    [Fact]
    public async Task ProgressIsAllowlistedAndExcludesOtherOrRemovedMappings()
    {
        await DocumentSynchronizationService.RequestSourcesAsync(Config, TimeProvider.System, _database, null, Token);
        await using (var db = _database.CreateDbContext())
        {
            foreach (var state in await db.DocumentSynchronizations.ToArrayAsync(Token))
            {
                state.Requested = false;
                state.State = DocumentSynchronizationStates.Failed;
                state.FailureCode = DocumentSynchronizationStates.StorageUnavailable;
                state.AddedCount = 1;
                state.RepairedCount = 2;
                state.RemovedCount = 3;
                state.UnchangedCount = 4;
                state.ConflictCount = 5;
                state.LastSucceededAtUtc = DateTimeOffset.UnixEpoch;
            }
            db.DocumentSynchronizations.Add(new DocumentSynchronizationEntity
            {
                Id = "retired-source",
                TenantId = "tenant-a",
                CollectionId = "retired",
                State = DocumentSynchronizationStates.Succeeded,
            });
            await db.SaveChangesAsync(Token);
        }
        using var http = await ClientAsync(_host);
        var client = new AdministrationApiClient(http, new AdministrationSessionState());
        var result = await client.GetDocumentSynchronizationAsync("tenant-a", Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Sources.Count);
        Assert.All(result.Value.Sources, source =>
        {
            Assert.Equal(DocumentSynchronizationStates.Failed, source.State);
            Assert.Equal(DocumentSynchronizationStates.StorageUnavailable, source.FailureCode);
            Assert.Equal((1, 2, 3, 4, 5), (source.AddedCount, source.RepairedCount, source.RemovedCount, source.UnchangedCount, source.ConflictCount));
            Assert.Equal(DateTimeOffset.UnixEpoch, source.LastSucceededAtUtc);
        });
    }

    [Fact]
    public async Task OpenDevelopmentCannotSynchronizeSources()
    {
        await using var host = new AgentDefinitionAdministrationWebApplicationFactory();
        using var http = host.CreateClient();
        using var read = await http.GetAsync(Path("default"), Token);
        using var request = await http.PostAsJsonAsync(Path("default"), new { }, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, request.StatusCode);
        await AssertNoWorkAsync();
    }

    private async Task AssertNoWorkAsync()
    {
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.DocumentSynchronizations.ToArrayAsync(Token));
        Assert.Empty(await db.KnowledgeDocuments.ToArrayAsync(Token));
        Assert.Empty(await db.DocumentIngestionJobs.ToArrayAsync(Token));
    }

    private static string Path(string app) => AdministrationRoutes.Format(DocumentCatalogueRoutes.Synchronization, app);

    private static async Task<HttpClient> ClientAsync(DocumentReviewWebApplicationFactory host)
    {
        var http = host.CreateClient();
        await AdministrationTestClient.PrepareAsync(http, AdministrationSignInWebApplicationFactory.DefaultAppAdministrator);
        return http;
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }
}
#endif
