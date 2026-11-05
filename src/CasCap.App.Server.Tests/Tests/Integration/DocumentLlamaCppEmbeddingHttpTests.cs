using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Exercises llama.cpp dispatch with synthetic tenant policy and a non-network single-model server.</summary>
[Trait("Category", "Indexing")]
[Trait("Category", "Integration")]
public sealed class DocumentLlamaCppEmbeddingHttpTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("http-error")]
    [InlineData("redirect")]
    [InlineData("changed-after")]
    [InlineData("properties-changed-after")]
    [InlineData("too-large")]
    [InlineData("malformed")]
    [InlineData("content-type")]
    [InlineData("cancelled")]
    public async Task SubmitsExactInputsOnceWithoutModelManagementOrReplay(string variation)
    {
        await using var harness = new Harness(variation);
        await harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Handler.Submissions);
        if (variation == "valid")
        {
            var result = await harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken);
            Assert.Equal(2, result.Vectors.Count);
            Assert.Equal(8, result.Vectors[0].Length);
            Assert.Equal(9, result.InputTokens);
            Assert.Equal(3, harness.Handler.ModelReads);
            Assert.Equal(3, harness.Handler.PropertiesReads);
        }
        else if (variation is "http-error" or "redirect")
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
            Assert.Equal(variation == "http-error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TemporaryRedirect, error.StatusCode);
            Assert.DoesNotContain("private-response-body", error.Message, StringComparison.Ordinal);
        }
        else if (variation == "cancelled")
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
        Assert.Equal(1, harness.Handler.Submissions);
    }

    [Theory]
    [InlineData("missing-tenant")]
    [InlineData("missing-provider")]
    [InlineData("provider-type")]
    [InlineData("endpoint")]
    [InlineData("authority")]
    [InlineData("credential")]
    [InlineData("entra")]
    [InlineData("ambiguous")]
    [InlineData("unpinned")]
    public async Task AuthorizationFailsBeforeAnyHttp(string variation)
    {
        await using var harness = new Harness(variation);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Handler.ModelReads);
        Assert.Equal(0, harness.Handler.PropertiesReads);
        Assert.Equal(0, harness.Handler.Submissions);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("empty-models")]
    [InlineData("router")]
    [InlineData("null-model")]
    [InlineData("null-meta")]
    [InlineData("dimensions")]
    [InlineData("properties-model")]
    [InlineData("router-properties")]
    [InlineData("metadata-http")]
    [InlineData("metadata-too-large")]
    [InlineData("metadata-content-type")]
    public async Task MetadataMustMatchBeforeEmbeddingSubmission(string variation)
    {
        await using var harness = new Harness(variation);
        if (variation == "metadata-http")
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Handler.Submissions);
    }

    [Fact]
    public async Task EmptyBatchFailsBeforeHttp()
    {
        await using var harness = new Harness("valid");
        var batch = harness.Batch with { Inputs = [] };
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Adapter.GenerateAsync(batch, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Handler.ModelReads);
        Assert.Equal(0, harness.Handler.Submissions);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _container;

        public DocumentEmbeddingBatch Batch { get; }
        public LlamaCppHandler Handler { get; }
        public DocumentEmbeddingProvider Adapter { get; }

        public Harness(string variation)
        {
            var manifest = DocumentIndexingPlanBuilder.Build(DocumentIndexingTestData.LlamaCppConfig(), DocumentIndexingTestData.Review()).Manifest!;
            if (variation is "ambiguous" or "unpinned")
                manifest = manifest with
                {
                    Profile = variation == "ambiguous" ? manifest.Profile with { Azure = DocumentIndexingTestData.Azure }
                        : manifest.Profile with { Model = "mutable-model-tag" },
                };
            Batch = new(new("example-index", Guid.NewGuid(), DateTimeOffset.MaxValue, manifest), 0, manifest.Inputs.Take(2).ToArray());
            Handler = new(Batch, variation);
            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants = new()
                {
                    [variation == "missing-tenant" ? "another-tenant" : manifest.TenantId] = new()
                    {
                        Providers = new()
                        {
                            [variation == "missing-provider" ? "another-provider" : manifest.Profile.Provider] = new()
                            {
                                Type = variation == "provider-type" ? AgentType.Ollama : AgentType.OpenAI,
                                ModelName = "unused-chat-model",
                                Endpoint = new(variation == "endpoint" ? "http://other.example.com/" : manifest.Profile.LlamaCpp!.Endpoint),
                                ApiKey = variation == "credential" ? "synthetic-key" : null,
                            },
                        },
                        EntraIdProviders = variation == "entra" ? [manifest.Profile.Provider] : [],
                    },
                },
            }));
            services.AddSingleton(Options.Create(new AgentDefinitionPolicyConfig
            {
                AllowedProviderAuthorities = variation == "authority" ? [] : [new Uri(manifest.Profile.LlamaCpp!.Endpoint).GetLeftPart(UriPartial.Authority)],
            }));
            _container = services.BuildServiceProvider();
            var clients = new Clients(Handler);
            var credential = new RuntimeEntraCredential(() => throw new InvalidOperationException("llama.cpp must not request Azure credentials."));
            Adapter = new(NullLogger<DocumentEmbeddingProvider>.Instance, TimeProvider.System, _container, clients,
                new(clients, credential), new(_container, clients), new(_container, clients));
        }

        public async ValueTask DisposeAsync()
        {
            Handler.Dispose();
            await _container.DisposeAsync();
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class LlamaCppHandler(DocumentEmbeddingBatch batch, string variation) : HttpMessageHandler
    {
        public int ModelReads { get; private set; }
        public int PropertiesReads { get; private set; }
        public int Submissions { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("embedding.example.com", request.RequestUri!.Host);
            Assert.Equal(8080, request.RequestUri.Port);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("api-key"));
            if (request.RequestUri.AbsolutePath == "/v1/models")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                ModelReads++;
                return ModelsResponse();
            }
            if (request.RequestUri.AbsolutePath == "/props")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                PropertiesReads++;
                return PropertiesResponse();
            }
            Assert.Equal("/v1/embeddings", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Submissions++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(batch.Claim.Manifest.Profile.Model, body.RootElement.GetProperty("model").GetString());
            Assert.Equal("float", body.RootElement.GetProperty("encoding_format").GetString());
            Assert.Equal(3, body.RootElement.EnumerateObject().Count());
            Assert.Equal(batch.Inputs.Select(input => input.Chunk.EmbeddingText),
                body.RootElement.GetProperty("input").EnumerateArray().Select(input => input.GetString()));
            if (variation is "http-error" or "redirect")
                return Response("private-response-body", variation == "http-error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TemporaryRedirect);
            if (variation == "cancelled")
                throw new OperationCanceledException(cancellationToken);
            if (variation == "malformed")
                return Response("private-response-body");
            var result = Response(JsonSerializer.Serialize(new
            {
                model = batch.Claim.Manifest.Profile.Model,
                data = batch.Inputs.Select((_, index) => new { index, embedding = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 } }),
                usage = new { prompt_tokens = 9, total_tokens = 9 },
            }));
            if (variation == "too-large")
                result.Content.Headers.ContentLength = 16 * 1024 * 1024 + 1;
            if (variation == "content-type")
                result.Content.Headers.ContentType = new("text/plain");
            return result;
        }

        private HttpResponseMessage ModelsResponse()
        {
            if (variation == "metadata-http")
                return Response("private-response-body", HttpStatusCode.ServiceUnavailable);
            var alias = variation == "model" || variation == "changed-after" && Submissions > 0
                ? "another-model" : batch.Claim.Manifest.Profile.Model;
            object? entry = variation == "null-model" ? null : new
            {
                id = alias,
                meta = variation == "null-meta" ? null : new { n_embd = variation == "dimensions" ? 9 : 8 },
            };
            var response = Response(JsonSerializer.Serialize(new
            {
                data = variation == "empty-models" ? Array.Empty<object?>() : variation == "router" ? new[] { entry, entry } : [entry],
            }));
            if (variation == "metadata-too-large")
                response.Content.Headers.ContentLength = 1024 * 1024 + 1;
            if (variation == "metadata-content-type")
                response.Content.Headers.ContentType = new("text/plain");
            return response;
        }

        private HttpResponseMessage PropertiesResponse() =>
            Response(JsonSerializer.Serialize(new
            {
                model_alias = variation == "router-properties" ? null
                    : variation == "properties-model" || variation == "properties-changed-after" && Submissions > 0 ? "another-model"
                    : batch.Claim.Manifest.Profile.Model,
            }));

        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
