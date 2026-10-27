#if DEBUG
using CasCap.Tests.Unit;
using CasCap.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the worker's real executor/SDK adapter without HTTP-request tenancy, a database or a live provider.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "HybridPreparation")]
public sealed class DocumentPreparationProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualWorkerAdapterUsesPinnedSchemaAndTenantCredentials(bool forgedTenant)
    {
        using var handler = new DocumentPreparationAgentTemplateTests.PreparationHandler { ExpectedCredential = "synthetic-key-a" };
        using var client = new HttpClient(handler);
        var clients = new Clients(client);
        var credentials = new RuntimeEntraCredential(() => null);
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<TimeProvider>(new UnusedClock())
            .AddSingleton<ITenantContext, UnusedTenant>()
            .AddSingleton<IDbContextFactory<AgentRuntimeDbContext>, UnusedDatabase>()
            .AddSingleton(Options.Create(new ApiAuthConfig { Username = "unused", Password = "unused" }))
            .AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants = new()
                {
                    ["tenant-a"] = Tenant("synthetic-key-a"),
                    ["tenant-b"] = Tenant("synthetic-key-b"),
                },
            }))
            .AddSingleton(credentials);
        await using var container = services.BuildServiceProvider();
        var provider = new DocumentPreparationProvider(container, clients, credentials, new DocumentPreparationModelVerifier(clients, credentials));
        var plan = DocumentHybridPreparationTestData.Plan;
        var request = new DocumentPreparationPageRequest
        {
            TenantId = forgedTenant ? "tenant-b" : plan.TenantId,
            Plan = plan with { Settings = plan.Settings with { MaximumPages = 7, MaximumOutputTokens = 1000 } },
            PageNumber = 7,
            Pdf = DocumentPreparationTestData.Pdf("Synthetic source"),
            OriginalSha256 = new string('A', 64),
            Draft = "Untrusted draft",
        };
        AgentStructuredResponse? receipt = null;
        async Task ExecuteAsync() => await provider.ExecutePageAsync(request, (response, _) =>
        {
            receipt = response;
            return ValueTask.CompletedTask;
        }, TestContext.Current.CancellationToken);
        if (forgedTenant)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(ExecuteAsync);
            Assert.Null(handler.Body);
            Assert.Null(receipt);
            return;
        }
        await ExecuteAsync();
        Assert.NotNull(receipt);
        Assert.Equal(15, receipt.Usage?.TotalTokenCount);
        using var body = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        var root = body.RootElement;
        Assert.Equal("synthetic-deployment", root.GetProperty("model").GetString());
        Assert.Equal(1000, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(7, root.GetProperty("text").GetProperty("format").GetProperty("schema")
            .GetProperty("properties").GetProperty("page").GetProperty("enum")[0].GetInt32());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.GetProperty("background").GetBoolean());
        Assert.DoesNotContain("synthetic-key", handler.Body, StringComparison.Ordinal);
    }

    private static TenantAgentConfig Tenant(string apiKey) => new()
    {
        Providers = new()
        {
            ["synthetic-provider"] = DocumentHybridPreparationTestData.Plan.Definition.Provider with { ApiKey = apiKey },
        },
    };

    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class UnusedTenant : ITenantContext
    {
        public string TenantId => throw new InvalidOperationException("The worker must not use HTTP tenancy.");
    }

    private sealed class UnusedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Preparation must not inject runtime time context.");
    }

    private sealed class UnusedDatabase : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => throw new InvalidOperationException("The pinned definition must not be re-resolved.");
    }
}
#endif
