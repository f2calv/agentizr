#if DEBUG
using System.Text;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Exercises the actual provider dispatcher with synthetic tenant policy and a non-network Ollama handler.</summary>
[Trait("Category", "Indexing")]
[Trait("Category", "Integration")]
public sealed class DocumentOllamaEmbeddingHttpTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("http-error")]
    [InlineData("changed-after")]
    [InlineData("too-large")]
    [InlineData("malformed")]
    [InlineData("cancelled")]
    public async Task OllamaSubmitsExactIncludedInputsOnceWithoutTruncation(string variation)
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
            Assert.Equal(4, harness.Handler.TagReads);
            Assert.Equal(1, harness.Handler.ShowReads);
        }
        else if (variation == "http-error")
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("private-response-body", error.Message, StringComparison.Ordinal);
        }
        else if (variation == "cancelled")
            await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => harness.Adapter.GenerateAsync(harness.Batch, TestContext.Current.CancellationToken));
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
    public async Task TenantAuthorizationFailsBeforeAnyHttp(string variation)
    {
        await using var harness = new Harness(variation);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Handler.TagReads);
        Assert.Equal(0, harness.Handler.ShowReads);
        Assert.Equal(0, harness.Handler.Submissions);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("missing-model")]
    [InlineData("duplicate-tag")]
    [InlineData("remote")]
    [InlineData("remote-show")]
    [InlineData("chat-model")]
    [InlineData("dimensions")]
    [InlineData("metadata-drift")]
    public async Task InstalledModelMetadataMustMatchBeforeReservation(string variation)
    {
        await using var harness = new Harness(variation);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Adapter.VerifyAsync(harness.Batch.Claim.Manifest, TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Handler.Submissions);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _container;

        public DocumentEmbeddingBatch Batch { get; }
        public OllamaHandler Handler { get; }
        public DocumentEmbeddingProvider Adapter { get; }

        public Harness(string variation)
        {
            var settings = DocumentIndexingTestData.OllamaConfig();
            var manifest = DocumentIndexingPlanBuilder.Build(settings, DocumentIndexingTestData.Review()).Manifest!;
            Batch = new(new("example-index", Guid.NewGuid(), DateTimeOffset.MaxValue, manifest), 0, manifest.Inputs.Take(2).ToArray());
            Handler = new(Batch, variation);
            var provider = new ProviderConfig
            {
                Type = variation == "provider-type" ? AgentType.AzureOpenAI : AgentType.Ollama,
                ModelName = "qwen3.5:9b",
                Endpoint = new(variation == "endpoint" ? "http://other.example.com:11434/" : manifest.Profile.Ollama!.Endpoint),
                ApiKey = variation == "credential" ? "synthetic-key" : null,
            };
            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new AgentRuntimeConfig
            {
                Tenants = new()
                {
                    [variation == "missing-tenant" ? "another-tenant" : manifest.TenantId] = new()
                    {
                        Providers = new() { [variation == "missing-provider" ? "another-provider" : manifest.Profile.Provider] = provider },
                        EntraIdProviders = variation == "entra" ? [manifest.Profile.Provider] : [],
                    },
                },
            }));
            services.AddSingleton(Options.Create(new AgentDefinitionPolicyConfig
            {
                AllowedProviderAuthorities = variation == "authority" ? [] : [new Uri(manifest.Profile.Ollama!.Endpoint).GetLeftPart(UriPartial.Authority)],
            }));
            _container = services.BuildServiceProvider();
            var clients = new Clients(Handler);
            var credential = new RuntimeEntraCredential(() => throw new InvalidOperationException("Ollama must not request Azure credentials."));
            Adapter = new(_container, clients, new(clients, credential), new(_container, clients));
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

    private sealed class OllamaHandler(DocumentEmbeddingBatch batch, string variation) : HttpMessageHandler
    {
        public int TagReads { get; private set; }
        public int ShowReads { get; private set; }
        public int Submissions { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("ollama.example.com", request.RequestUri!.Host);
            Assert.Equal(11434, request.RequestUri.Port);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("api-key"));
            if (request.RequestUri.AbsolutePath == "/api/tags")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                TagReads++;
                var digest = variation == "digest" || variation == "changed-after" && Submissions > 0
                    || variation == "metadata-drift" && TagReads > 1 ? new string('b', 64) : batch.Claim.Manifest.Profile.ModelRevision;
                var entry = new { name = batch.Claim.Manifest.Profile.Model, digest, remote_host = variation == "remote" ? "https://remote.example.com" : null };
                return Response(JsonSerializer.Serialize(new { models = variation == "missing-model" ? [] : variation == "duplicate-tag" ? new[] { entry, entry } : [entry] }));
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(batch.Claim.Manifest.Profile.Model, body.RootElement.GetProperty("model").GetString());
            if (request.RequestUri.AbsolutePath == "/api/show")
            {
                ShowReads++;
                return Response(JsonSerializer.Serialize(new
                {
                    capabilities = new[] { variation == "chat-model" ? "completion" : "embedding" },
                    remote_model = variation == "remote-show" ? "example-remote-model" : null,
                    model_info = new Dictionary<string, object>
                    {
                        ["general.architecture"] = "qwen3",
                        ["qwen3.embedding_length"] = variation == "dimensions" ? 9 : 8,
                    },
                }));
            }
            Assert.Equal("/api/embed", request.RequestUri.AbsolutePath);
            Submissions++;
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.Equal("0", body.RootElement.GetProperty("keep_alive").GetString());
            Assert.False(body.RootElement.TryGetProperty("dimensions", out _));
            Assert.Equal(batch.Inputs.Select(input => input.Chunk.EmbeddingText),
                body.RootElement.GetProperty("input").EnumerateArray().Select(input => input.GetString()));
            if (variation == "http-error")
                return Response("private-response-body", HttpStatusCode.ServiceUnavailable);
            if (variation == "cancelled")
                throw new OperationCanceledException(cancellationToken);
            if (variation == "malformed")
                return Response("private-response-body");
            var response = Response(JsonSerializer.Serialize(new
            {
                model = batch.Claim.Manifest.Profile.Model,
                embeddings = batch.Inputs.Select(_ => new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
                prompt_eval_count = 9,
            }));
            if (variation == "too-large")
                response.Content.Headers.ContentLength = 16 * 1024 * 1024 + 1;
            return response;
        }

        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
#endif
