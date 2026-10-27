using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasCap.Tests.Unit;

/// <summary>Checks the shipped preparation contract without installing a definition or contacting a provider.</summary>
[Trait("Category", "Document Preparation")]
public sealed class DocumentPreparationAgentTemplateTests
{
    private static readonly JsonSerializerOptions DefinitionOptions = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void TemplateIsBoundedToolFreeAndUnconfigured()
    {
        var template = DocumentPreparationAgentTemplate.Create();
        var agent = template.Definition.GetProperty("agent");
        var provider = template.Definition.GetProperty("provider");

        Assert.Equal("document-preparation", template.AgentName);
        Assert.Equal("PdfReconciliationV1", template.TemplateVersion);
        Assert.False(agent.GetProperty("includeRuntimeContext").GetBoolean());
        Assert.True(agent.GetProperty("stateless").GetBoolean());
        Assert.Equal(8192, agent.GetProperty("maximumOutputTokens").GetInt32());
        Assert.Empty(agent.GetProperty("tools").EnumerateArray());
        Assert.True(provider.GetProperty("useResponsesApi").GetBoolean());
        Assert.Equal(string.Empty, provider.GetProperty("modelName").GetString());
        Assert.Equal(JsonValueKind.Null, provider.GetProperty("endpoint").ValueKind);
        Assert.Equal(JsonValueKind.Null, provider.GetProperty("apiKey").ValueKind);
        Assert.Equal(string.Empty, agent.GetProperty("provider").GetString());
        var schema = agent.GetProperty("jsonOutput").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["page", "markdown", "changes", "uncertainties"],
            schema.GetProperty("required").EnumerateArray().Select(property => property.GetString()));
#if DEBUG
        Assert.Equal(OpenAIDocumentQualityClient.Instructions, agent.GetProperty("instructions").GetString());
        Assert.Equal(OpenAIDocumentQualityClient.MaximumOutputTokens, DocumentPreparationAgentTemplate.MaximumOutputTokens);
#endif
    }

    [Fact]
    public void EditorPreservesTemplateConstraintsInPublishedPayload()
    {
        var draft = DefinitionDraft.FromDefinition(DocumentPreparationAgentTemplate.Create().Definition);
        var form = draft.ToForm("preparation");
        form.ProviderName = "synthetic-provider";
        form.Endpoint = "https://example.com/";
        form.ModelName = "synthetic-model";

        Assert.True(draft.TryCreatePayload(form, out var payload, out var error), error);
        var agent = payload.GetProperty("agent");
        Assert.True(agent.GetProperty("stateless").GetBoolean());
        Assert.False(agent.GetProperty("includeRuntimeContext").GetBoolean());
        Assert.Equal(8192, agent.GetProperty("maximumOutputTokens").GetInt32());
        Assert.Equal(JsonValueKind.Object, agent.GetProperty("jsonOutput").GetProperty("schema").ValueKind);
        Assert.True(payload.GetProperty("provider").GetProperty("useResponsesApi").GetBoolean());
        Assert.False(payload.GetProperty("provider").TryGetProperty("apiKey", out _));
        var config = agent.Deserialize<AgentConfig>(DefinitionOptions);
        Assert.NotNull(config);
        Assert.False(config.IncludeRuntimeContext);
        Assert.True(config.Stateless);
    }

    [Fact]
    public async Task RuntimeExecutionHasNoDiscoveryToolsTimeContextOverridesOrSession()
    {
        var agent = Assert.IsType<AgentConfig>(DocumentPreparationAgentTemplate.Create().Definition
            .GetProperty("agent").Deserialize<AgentConfig>(JsonSerializerOptions.Web));
        var definition = new AgentDefinition
        {
            Name = "preparation",
            Version = "pinned-v1",
            Agent = agent,
            Provider = new ProviderConfig
            {
                Type = AgentType.OpenAI,
                Endpoint = new Uri("https://example.com/v1/"),
                ModelName = "synthetic-model",
                UseResponsesApi = true,
                ReasoningEffort = Microsoft.Extensions.AI.ReasoningEffort.Low,
            },
        };
        using var handler = new PreparationHandler();
        using var client = new HttpClient(handler);
        using var services = new ServiceCollection().BuildServiceProvider();
        var tenant = new TestTenantContext();
        var definitions = new UnusedDefinitionStore();
        var credentials = new EmptyCredentials();
        var runtime = Options.Create(new AgentRuntimeConfig());
        var executor = new CommonAgentExecutor(new TestHttpClientFactory(client), NullLoggerFactory.Instance,
            services, definitions,
            new AgentRuntimeBuiltInTools(NullLogger<AgentRuntimeBuiltInTools>.Instance, runtime,
                new UnusedTimeProvider(), tenant, definitions),
            credentials, credentials, tenant, runtime);
        var result = await executor.ExecuteAsync(new AgentExecutionContext
        {
            Definition = definition,
            ProviderApiKey = "synthetic-key",
            Request = new AgentExecutionRequest
            {
                AgentName = "preparation",
                SessionId = "job",
                Input = """{"physical_page":7}""",
                BinaryContent = "%PDF-synthetic"u8.ToArray(),
                MimeType = "application/pdf",
            },
            Overrides = new AgentOverrideState { ModelName = "wrong-model", Instructions = "wrong instructions" },
            SessionStateJson = "not valid session JSON",
            HistorySink = (_, _) => throw new InvalidOperationException("Preparation must not archive document content."),
        }, TestContext.Current.CancellationToken);

        Assert.Equal("pinned-v1", result.DefinitionVersion);
        Assert.Null(result.SessionStateJson);
        Assert.Equal(0, result.Diagnostics?.ToolCallCount);
        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        var root = request.RootElement;
        Assert.Equal("synthetic-model", root.GetProperty("model").GetString());
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(DocumentPreparationAgentTemplate.Instructions, root.GetProperty("instructions").GetString());
        Assert.False(root.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.DoesNotContain("Current local time", handler.Body, StringComparison.Ordinal);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public string TenantId => "synthetic-tenant";
    }

    private sealed class UnusedDefinitionStore : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Preparation must not discover other agents.");
    }

    private sealed class UnusedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            throw new InvalidOperationException("Preparation must not add current-time context.");
    }

    private sealed class EmptyCredentials : IProviderCredentialStore, IMcpCredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public Azure.Core.TokenCredential? GetTokenCredential(string providerName) => null;

        public ValueTask<string?> GetAuthorizationHeaderAsync(string credentialName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class PreparationHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"resp_synthetic","object":"response","created_at":1,"status":"completed","model":"synthetic-model",
                    "output":[{"type":"message","id":"msg_synthetic","status":"completed","role":"assistant",
                    "content":[{"type":"output_text","text":"{\"page\":7,\"markdown\":\"synthetic\",\"changes\":[],\"uncertainties\":[]}","annotations":[]}]}],
                    "usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}
                    """, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
