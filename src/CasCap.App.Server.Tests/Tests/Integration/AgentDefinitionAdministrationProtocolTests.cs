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
            AgentDefinitionTestData.CreatePublishRequest("v2", "model-v2"),
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
        var activation = Assert.Single(activations);
        Assert.Equal("development", activation.ActivatedBy);
        Assert.Equal("promote v2", activation.ChangeReason);
        Assert.True(rolledBack);
        Assert.Equal("test-v1", afterRollback?.DefinitionVersion);
    }

    [Fact]
    public async Task PublishDefinition_RejectsProviderCredentials()
    {
        using var httpClient = factory.CreateClient();
        var request = AgentDefinitionTestData.CreatePublishRequest("credential-attempt", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = AgentDefinitionTestData.CreateAgent(),
                provider = AgentDefinitionTestData.CreateProvider("model") with { ApiKey = "must-not-cross-boundary" },
            }, JsonSerializerOptions.Web),
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EvaluateDefinition_GradesAnInactiveVersionWithoutActivatingIt()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentDefinitionAdminClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;
        await client.PublishAsync(
            "evaluated",
            AgentDefinitionTestData.CreatePublishRequest("candidate", "model"),
            cancellationToken);

        var passing = await client.EvaluateAsync("evaluated", "candidate", CreateEvaluation(42), cancellationToken);
        var failing = await client.EvaluateAsync("evaluated", "candidate", CreateEvaluation(7), cancellationToken);
        var missing = await client.EvaluateAsync("evaluated", "missing", CreateEvaluation(42), cancellationToken);

        Assert.True(passing?.Passed);
        Assert.Equal("The answer is 42.", Assert.Single(passing!.Runs).Answer);
        Assert.False(failing?.Passed);
        Assert.Null(missing);
        Assert.Null(await client.GetActiveAsync("evaluated", cancellationToken));
    }

    [Fact]
    public async Task ActivateDefinition_RefusesAFailingEvaluationAndAcceptsAPassingOne()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentDefinitionAdminClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;
        await client.PublishAsync(
            "gated",
            AgentDefinitionTestData.CreatePublishRequest("candidate", "model"),
            cancellationToken);

        using var refused = await httpClient.PutAsJsonAsync(
            "/api/v1/agents/gated/definitions/candidate/activate",
            new ActivateAgentDefinitionRequest { ChangeReason = "gate", Evaluation = CreateEvaluation(7) },
            cancellationToken);
        var activeAfterRefusal = await client.GetActiveAsync("gated", cancellationToken);
        var activated = await client.ActivateAsync(
            "gated",
            "candidate",
            new ActivateAgentDefinitionRequest { ChangeReason = "gate", Evaluation = CreateEvaluation(42) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Evaluation passed 0 of 1 runs", await refused.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.Null(activeAfterRefusal);
        Assert.True(activated);
        Assert.Equal("candidate", (await client.GetActiveAsync("gated", cancellationToken))?.DefinitionVersion);
    }

    private static EvaluateAgentDefinitionRequest CreateEvaluation(double expected) => new()
    {
        Scenarios =
        [
            new AgentEvaluationScenarioRequest
            {
                Id = "answer",
                Question = "What is the answer?",
                Answer = new AgentEvaluationAnswerRequest { Number = expected },
            },
        ],
    };

    [Fact]
    public async Task DeactivateDefinition_RefusesWhileDelegatedToThenRecordsTheDeactivation()
    {
        using var httpClient = factory.CreateClient();
        var client = new AgentDefinitionAdminClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;
        await client.PublishAsync("worker", AgentDefinitionTestData.CreatePublishRequest("v1", "model"), cancellationToken);
        await client.ActivateAsync("worker", "v1", new ActivateAgentDefinitionRequest(), cancellationToken);
        await client.PublishAsync("lead", AgentDefinitionTestData.CreatePublishRequest("v1", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = AgentDefinitionTestData.CreateAgent("lead", tools: [new ToolSource { Agent = "worker" }]),
                provider = AgentDefinitionTestData.CreateProvider("model"),
            }, JsonSerializerOptions.Web),
        }, cancellationToken);
        await client.ActivateAsync("lead", "v1", new ActivateAgentDefinitionRequest(), cancellationToken);
        var retire = new DeactivateAgentDefinitionRequest { ChangeReason = "retire" };

        using var refused = await httpClient.PostAsJsonAsync("/api/v1/agents/worker/definitions/active/deactivate", retire, cancellationToken);
        var leadDeactivated = await client.DeactivateAsync("lead", retire, cancellationToken);
        var workerDeactivated = await client.DeactivateAsync("worker", retire, cancellationToken);
        var repeated = await client.DeactivateAsync("worker", retire, cancellationToken);
        var history = await client.GetActivationHistoryAsync("worker", 10, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("lead", await refused.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.True(leadDeactivated);
        Assert.True(workerDeactivated);
        Assert.False(repeated);
        Assert.Null(await client.GetActiveAsync("worker", cancellationToken));
        Assert.True(history[0].IsDeactivation);
        Assert.Equal("retire", history[0].ChangeReason);
        Assert.False(history[1].IsDeactivation);
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
        var request = AgentDefinitionTestData.CreatePublishRequest("duplicate", "model");
        using var first = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/duplicate-agent/definitions",
            request,
            TestContext.Current.CancellationToken);
        using var second = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/duplicate-agent/definitions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("/api/v1/agents/duplicate-agent/definitions/duplicate", first.Headers.Location?.OriginalString);
        using var stored = await httpClient.GetAsync(first.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PublishDefinition_ReservedVersionReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/assistant/definitions",
            AgentDefinitionTestData.CreatePublishRequest("active", "model"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("invalid")]
    public async Task GetDefinitionHistory_InvalidLimitReturnsBadRequest(string limit)
    {
        using var httpClient = factory.CreateClient();

        using var response = await httpClient.GetAsync(
            $"/api/v1/agents/assistant/definitions?limit={limit}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishDefinition_UnsupportedSchemaReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();
        var request = AgentDefinitionTestData.CreatePublishRequest("unsupported-schema", "model") with { SchemaVersion = 2 };

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
        var request = AgentDefinitionTestData.CreatePublishRequest("missing-delegation", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = AgentDefinitionTestData.CreateAgent() with { Tools = [new ToolSource { Agent = "missing-agent" }] },
                provider = AgentDefinitionTestData.CreateProvider("model"),
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

    [Fact]
    public async Task ActivateDefinition_MissingMcpCredentialReturnsBadRequest()
    {
        using var httpClient = factory.CreateClient();
        var request = AgentDefinitionTestData.CreatePublishRequest("missing-mcp-credential", "model") with
        {
            Definition = JsonSerializer.SerializeToElement(new
            {
                agent = AgentDefinitionTestData.CreateAgent() with
                {
                    Tools =
                    [
                        new ToolSource
                        {
                            Endpoint = "https://mcp.example.com/mcp",
                            Credential = "missing-credential",
                        },
                    ],
                },
                provider = AgentDefinitionTestData.CreateProvider("model"),
            }, JsonSerializerOptions.Web),
        };
        using var publishResponse = await httpClient.PostAsJsonAsync(
            "/api/v1/agents/mcp-agent/definitions",
            request,
            TestContext.Current.CancellationToken);

        using var activateResponse = await httpClient.PutAsJsonAsync(
            "/api/v1/agents/mcp-agent/definitions/missing-mcp-credential/activate",
            new ActivateAgentDefinitionRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, publishResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, activateResponse.StatusCode);
    }

    private static PublishAgentDefinitionRequest CreateUnsafePublishRequest(string scenario)
    {
        var agent = AgentDefinitionTestData.CreateAgent();
        var provider = AgentDefinitionTestData.CreateProvider("model");
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

}
