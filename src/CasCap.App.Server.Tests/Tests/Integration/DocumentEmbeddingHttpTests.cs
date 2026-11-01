#if DEBUG
using Azure.Core;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the real Azure adapter against a fake HTTP handler and synthetic tenant credentials.</summary>
[Trait("Category", "Indexing")]
[Trait("Category", "Integration")]
public sealed class DocumentEmbeddingHttpTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AzureAdapterBindsTenantDeploymentExactInputsAndOneSubmission(bool entra, bool fail)
    {
        var settings = DocumentIndexingTestData.ExecutionConfig();
        var manifest = DocumentIndexingPlanBuilder.Build(settings, DocumentIndexingTestData.Review()).Manifest!;
        var batch = new DocumentEmbeddingBatch(new("example-index", Guid.NewGuid(), DateTimeOffset.MaxValue, manifest),
            0, manifest.Inputs.Take(2).ToArray());
        var credential = new Credential();
        var services = new ServiceCollection();
        services.AddSingleton(new RuntimeEntraCredential(() => credential));
        services.AddSingleton(Options.Create(new AgentRuntimeConfig
        {
            Tenants = new()
            {
                [manifest.TenantId] = new()
                {
                    Providers = new()
                    {
                        [manifest.Profile.Provider] = DocumentHybridPreparationTestData.Plan.Definition.Provider with
                        {
                            Endpoint = new(DocumentIndexingTestData.Azure.Endpoint),
                            ApiKey = entra ? null : "synthetic-embedding-key",
                        },
                    },
                    EntraIdProviders = entra ? [manifest.Profile.Provider] : [],
                },
            },
        }));
        services.AddSingleton(Options.Create(new AgentDefinitionPolicyConfig
        {
            AllowedProviderAuthorities = [new Uri(DocumentIndexingTestData.Azure.Endpoint).GetLeftPart(UriPartial.Authority)],
        }));
        await using var container = services.BuildServiceProvider();
        using var handler = new AzureHandler(batch, entra, fail);
        var clients = new Clients(handler);
        var adapter = new DocumentEmbeddingProvider(container, clients, new(clients, container.GetRequiredService<RuntimeEntraCredential>()),
            new(container, clients));
        await adapter.VerifyAsync(manifest, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.MetadataReads);
        Assert.Equal(0, handler.Submissions);
        if (fail)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => adapter.GenerateAsync(batch, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("private-response-body", error.Message, StringComparison.Ordinal);
        }
        else
        {
            var result = await adapter.GenerateAsync(batch, TestContext.Current.CancellationToken);
            Assert.Equal(batch.Inputs.Count, result.Vectors.Count);
            Assert.Equal(8, result.Vectors[0].Length);
            Assert.Equal(9, result.InputTokens);
        }
        Assert.Equal(1, handler.Submissions);
        Assert.Contains("https://management.azure.com/.default", credential.Scopes);
        if (entra)
            Assert.Contains("https://cognitiveservices.azure.com/.default", credential.Scopes);
    }

    private sealed class Credential : TokenCredential
    {
        public List<string> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes.AddRange(requestContext.Scopes);
            return new("synthetic-access-token", DateTimeOffset.MaxValue);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class AzureHandler(DocumentEmbeddingBatch batch, bool entra, bool fail) : HttpMessageHandler
    {
        public int MetadataReads { get; private set; }

        public int Submissions { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var azure = batch.Claim.Manifest.Profile.Azure!;
            if (request.Method == HttpMethod.Get)
            {
                MetadataReads++;
                Assert.Equal("management.azure.com", request.RequestUri!.Host);
                var deployment = request.RequestUri.AbsolutePath.Contains("/deployments/", StringComparison.Ordinal);
                return Response(JsonSerializer.Serialize(new
                {
                    id = deployment ? azure.DeploymentResourceId : azure.DeploymentResourceId[..azure.DeploymentResourceId.LastIndexOf("/deployments/", StringComparison.Ordinal)],
                    name = azure.DeploymentName,
                    properties = new
                    {
                        provisioningState = "Succeeded",
                        endpoint = azure.Endpoint,
                        model = new { format = "OpenAI", name = batch.Claim.Manifest.Profile.Model, version = batch.Claim.Manifest.Profile.ModelRevision },
                    },
                }));
            }
            Submissions++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("embedding.example.com", request.RequestUri!.Host);
            Assert.Equal("/openai/deployments/example-deployment/embeddings", request.RequestUri.AbsolutePath);
            Assert.Equal("?api-version=2024-10-21", request.RequestUri.Query);
            Assert.Equal(batch.Claim.ClaimId.ToString("D"), Assert.Single(request.Headers.GetValues("x-ms-client-request-id")));
            if (entra)
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            else
                Assert.True(request.Headers.TryGetValues("api-key", out var keys) && keys.Single() == "synthetic-embedding-key");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(batch.Inputs.Select(input => input.Chunk.EmbeddingText),
                body.RootElement.GetProperty("input").EnumerateArray().Select(input => input.GetString()));
            Assert.Equal(8, body.RootElement.GetProperty("dimensions").GetInt32());
            Assert.Equal("float", body.RootElement.GetProperty("encoding_format").GetString());
            if (fail)
                return Response("private-response-body", HttpStatusCode.ServiceUnavailable);
            return Response(JsonSerializer.Serialize(new
            {
                data = batch.Inputs.Select((_, index) => new { index, embedding = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 } }),
                usage = new { prompt_tokens = 9, total_tokens = 9 },
            }));
        }

        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
#endif
