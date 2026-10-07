using System.Text.Json;

namespace CasCap.IntegrationTests;

/// <summary>Exercises immutable definition administration through the typed control-plane client.</summary>
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AgentDefinitionAdministrationProtocolTests(
    AgentDefinitionAdministrationWebApplicationFactory factory)
    : IClassFixture<AgentDefinitionAdministrationWebApplicationFactory>
{
    [Fact]
    public async Task DefinitionLifecycle_PublishesActivatesAndRollsBack()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentDefinitionAdminClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await client.PublishAsync(
            "assistant",
            CreatePublishRequest("v2", "model-v2"),
            cancellationToken);
        var beforeActivation = await client.GetActiveAsync("assistant", cancellationToken);
        var historyBeforeActivation = await client.GetHistoryAsync("assistant", 10, cancellationToken);

        var activated = await client.ActivateAsync(
            "assistant",
            "v2",
            new ActivateAgentDefinitionRequest { ChangeReason = "promote v2" },
            cancellationToken);
        var active = await client.GetActiveAsync("assistant", cancellationToken);
        var activations = await client.GetActivationHistoryAsync("assistant", 10, cancellationToken);
        var rolledBack = await client.ActivateAsync(
            "assistant",
            "test-v1",
            new ActivateAgentDefinitionRequest { ChangeReason = "rollback" },
            cancellationToken);
        var afterRollback = await client.GetActiveAsync("assistant", cancellationToken);

        Assert.False(published.IsActive);
        Assert.Equal("development", published.PublishedBy);
        Assert.Equal("v2", published.Definition.GetProperty("version").GetString());
        Assert.Equal("test-v1", beforeActivation?.DefinitionVersion);
        Assert.Equal(2, historyBeforeActivation.Count);
        Assert.Single(historyBeforeActivation, item => item.IsActive && item.DefinitionVersion == "test-v1");
        Assert.True(activated);
        Assert.Equal("v2", active?.DefinitionVersion);
        Assert.Single(activations);
        Assert.Equal("development", activations[0].ActivatedBy);
        Assert.Equal("promote v2", activations[0].ChangeReason);
        Assert.True(rolledBack);
        Assert.Equal("test-v1", afterRollback?.DefinitionVersion);
    }

    [Fact]
    public async Task PublishDefinition_RejectsProviderCredentials()
    {
        using var httpClient = factory.CreateClient();
        var request = CreatePublishRequest("credential-attempt", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = CreateAgentConfig(),
                provider = CreateProviderConfig("model") with { ApiKey = "must-not-cross-boundary" },
            }, JsonSerializerOptions.Web),
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActivateDefinition_UnknownVersionReturnsFalse()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentDefinitionAdminClient(httpClient);

        var activated = await client.ActivateAsync(
            "assistant",
            "missing",
            new ActivateAgentDefinitionRequest { ChangeReason = "invalid" },
            TestContext.Current.CancellationToken);

        Assert.False(activated);
    }

    [Theory]
    [InlineData("null-provider")]
    [InlineData("disallowed-provider")]
    [InlineData("file-instructions")]
    [InlineData("service-tool")]
    [InlineData("unknown-property")]
    [InlineData("relative-provider")]
    [InlineData("missing-instructions")]
    [InlineData("null-filters")]
    [InlineData("self-delegation")]
    public async Task PublishDefinition_RejectsUnsafeOrInvalidDocuments(string scenario)
    {
        using var httpClient = factory.CreateClient();
        var request = CreateUnsafePublishRequest(scenario);

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishDefinition_DuplicateVersionReturnsConflict()
    {
        using var httpClient = factory.CreateClient();
        var request = CreatePublishRequest("duplicate", "model");
        using var first = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/duplicate-agent/definitions",
            request,
            TestContext.Current.CancellationToken);
        using var second = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/duplicate-agent/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PublishDefinition_ReservedVersionReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            CreatePublishRequest("active", "model"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDefinitionHistory_InvalidLimitReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.GetAsync(
            "/api/v1/agents/assistant/definitions?limit=0",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishDefinition_UnsupportedSchemaReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();
        var request = CreatePublishRequest("unsupported-schema", "model") with { SchemaVersion = 2 };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActivateDefinition_MissingDelegatedAgentReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();
        var request = CreatePublishRequest("missing-delegation", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = CreateAgentConfig() with { Tools = [new ToolSource { Agent = "missing-agent" }] },
                provider = CreateProviderConfig("model"),
            }, JsonSerializerOptions.Web),
        };
        using var publishResponse = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/delegation-agent/definitions",
            request,
            TestContext.Current.CancellationToken);

        using var activateResponse = await httpClient.PutAsJsonAsync(
            "/api/v1/agents/delegation-agent/definitions/missing-delegation/activate",
            new ActivateAgentDefinitionRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, publishResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, activateResponse.StatusCode);
    }

    private static PublishAgentDefinitionRequest CreatePublishRequest(string version, string modelName) => new()
    {
        DefinitionVersion = version,
        SchemaVersion = 1,
        ChangeReason = "test publication",
        Definition = JsonSerializer.SerializeToElement(new
        {
            agent = CreateAgentConfig(),
            provider = CreateProviderConfig(modelName),
        }, JsonSerializerOptions.Web),
    };

    private static PublishAgentDefinitionRequest CreateUnsafePublishRequest(string scenario)
    {
        var agent = CreateAgentConfig();
        var provider = CreateProviderConfig("model");
        object document = scenario switch
        {
            "null-provider" => new { agent, provider = (ProviderConfig?)null },
            "disallowed-provider" => new
            {
                agent,
                provider = provider with { Endpoint = new Uri("https://untrusted.example.com") },
            },
            "file-instructions" => new
            {
                agent = agent with { InstructionsSource = "/var/run/secrets/example" },
                provider,
            },
            "service-tool" => new
            {
                agent = agent with { Tools = [new ToolSource { Service = "PrivilegedMcpQueryService" }] },
                provider,
            },
            "unknown-property" => new { agent, provider, unexpected = true },
            "relative-provider" => new
            {
                agent,
                provider = provider with { Endpoint = new Uri("relative", UriKind.Relative) },
            },
            "missing-instructions" => new
            {
                agent = agent with { Instructions = null },
                provider,
            },
            "null-filters" => new
            {
                agent = agent with
                {
                    Tools =
                    [
                        new ToolSource
                        {
                            Agent = "specialist",
                            IncludeTools = null!,
                            ExcludeTools = null!,
                        },
                    ],
                },
                provider,
            },
            "self-delegation" => new
            {
                agent = agent with { Tools = [new ToolSource { Agent = "assistant" }] },
                provider,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        return new PublishAgentDefinitionRequest
        {
            DefinitionVersion = $"unsafe-{scenario}",
            SchemaVersion = 1,
            Definition = JsonSerializer.SerializeToElement(document, JsonSerializerOptions.Web),
        };
    }

    private static AgentConfig CreateAgentConfig() => new()
    {
        Provider = "test",
        Name = "assistant",
        Description = "Test assistant",
        Prompt = "Test prompt",
        Instructions = "Answer test requests.",
    };

    private static ProviderConfig CreateProviderConfig(string modelName) => new()
    {
        Type = AgentType.Ollama,
        ModelName = modelName,
        Endpoint = new Uri("http://localhost:11434"),
    };
}
