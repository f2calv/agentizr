using Azure.Core;
using CasCap.Constants;
using CasCap.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace CasCap.IntegrationTests;

public sealed partial class PostgresAdministrationDocumentReviewTests
{
    [Theory]
    [InlineData("success", 1)]
    [InlineData("tokens", 0)]
    [InlineData("level", 0)]
    [InlineData("empty", 0)]
    [InlineData("provider", 1)]
    [InlineData("revoked", 1)]
    public async Task Knowledge_QueryReusesAzureAndNeverSubmitsForInvalidOrEmptySearches(string variation, int submissions)
    {
        var (store, binding, _) = await PublishedKnowledgeAsync();
        if (variation == "tokens")
            binding = binding with { MaximumQueryTokens = 1 };
        async Task RevokeAsync()
        {
            await using var db = _database.DbContextFactory.CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(Token);
            await DocumentIngestionTransactions.LockAsync(db, Token);
            (await db.KnowledgeDocuments.SingleAsync(Token)).Status = DocumentIngestionStates.Deleted;
            await db.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        if (variation == "empty")
            await RevokeAsync();
        var profile = DocumentIndexingTestData.ExecutionConfig().Profiles[0];
        var services = new ServiceCollection();
        services.AddSingleton(new RuntimeEntraCredential(() => new KnowledgeCredential()));
        services.AddSingleton(Options.Create(new AgentRuntimeConfig
        {
            Tenants = new()
            {
                [binding.TenantId] = new()
                {
                    Providers = new()
                    {
                        [profile.Provider] = DocumentHybridPreparationTestData.Plan.Definition.Provider with
                        {
                            Endpoint = new(profile.Azure!.Endpoint),
                            ApiKey = "synthetic-query-key",
                        },
                    },
                },
            },
        }));
        services.AddSingleton(Options.Create(new AgentDefinitionPolicyConfig
        {
            AllowedProviderAuthorities = [new Uri(profile.Azure!.Endpoint).GetLeftPart(UriPartial.Authority)],
        }));
        await using var container = services.BuildServiceProvider();
        using var handler = new KnowledgeHandler(profile, variation == "provider",
            variation == "revoked" ? RevokeAsync : null);
        var clients = new KnowledgeClients(handler);
        var provider = new DocumentEmbeddingProvider(NullLogger<DocumentEmbeddingProvider>.Instance, TimeProvider.System,
            container, clients, new(clients, container.GetRequiredService<RuntimeEntraCredential>()),
            new(container, clients), new(container, clients));
        var service = new DocumentKnowledgeService(NullLogger<DocumentKnowledgeService>.Instance,
            Options.Create(new DocumentKnowledgeConfig { Enabled = true, Bindings = [binding] }),
            new KnowledgeTenant(binding.TenantId), new KnowledgeDefinitions(), store, provider, new());
        var outcome = await service.SearchAsync(binding.AgentName, new()
        {
            Query = "synthetic short query",
            MaximumResults = 2,
            MaximumCefrLevel = variation == "level" ? "B1" : null,
        }, Token);
        Assert.Equal(submissions, handler.Submissions);
        Assert.Equal(submissions * 2, handler.MetadataReads);
        if (variation is "tokens" or "level")
        {
            Assert.NotNull(outcome.ValidationError);
            Assert.Null(outcome.Response);
        }
        else if (variation == "provider")
        {
            Assert.True(outcome.ProviderFailed);
            Assert.Null(outcome.Response);
        }
        else
        {
            Assert.NotNull(outcome.Response);
            Assert.Equal(variation == "empty" ? 0 : 3, outcome.Response.EmbeddingInputTokens);
            if (variation == "success")
                Assert.Equal(2, outcome.Response.Matches.Count);
            else
                Assert.Empty(outcome.Response.Matches);
        }
    }

    private sealed class KnowledgeTenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class KnowledgeDefinitions : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDefinition?>(DocumentHybridPreparationTestData.Plan.Definition);
    }

    private sealed class KnowledgeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("synthetic-query-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class KnowledgeClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class KnowledgeHandler(DocumentEmbeddingProfileConfig profile, bool fail, Func<Task>? beforeReceipt) : HttpMessageHandler
    {
        public int Submissions { get; private set; }

        public int MetadataReads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var azure = profile.Azure!;
            if (request.Method == HttpMethod.Get)
            {
                MetadataReads++;
                var deployment = request.RequestUri!.AbsolutePath.Contains("/deployments/", StringComparison.Ordinal);
                return Json(new
                {
                    id = deployment ? azure.DeploymentResourceId : azure.DeploymentResourceId[..azure.DeploymentResourceId.LastIndexOf("/deployments/", StringComparison.Ordinal)],
                    name = azure.DeploymentName,
                    properties = new
                    {
                        provisioningState = "Succeeded",
                        endpoint = azure.Endpoint,
                        model = new { format = "OpenAI", name = profile.Model, version = profile.ModelRevision },
                    },
                });
            }
            Submissions++;
            Assert.Equal("/openai/deployments/example-deployment/embeddings", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("synthetic short query", Assert.Single(body.RootElement.GetProperty("input").EnumerateArray()).GetString());
            Assert.Equal(profile.Dimensions, body.RootElement.GetProperty("dimensions").GetInt32());
            if (beforeReceipt is not null)
                await beforeReceipt().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return fail ? new(HttpStatusCode.TooManyRequests) : Json(new
            {
                data = new[] { new { index = 0, embedding = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 } } },
                usage = new { prompt_tokens = 3, total_tokens = 3 },
            });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
        };
    }
}
