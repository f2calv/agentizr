using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies fixture-backed definition evaluation never executes delegated agents and grades runs.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentDefinitionEvaluatorTests
{
    private static readonly AgentDefinition Specialist = AgentDefinitionTestData.CreateDefinition("specialist");
    private static readonly AgentDefinition Root = AgentDefinitionTestData.CreateDefinition(
        "root",
        tools: [new ToolSource { Agent = Specialist.Name }]);

    [Fact]
    public async Task EvaluateAsync_PassesWhenTheAnswerUsesTheDelegationFixture()
    {
        var factory = new ScriptedEvaluationAgentFactory(
            ScriptedEvaluationAgentFactory.CallTool("invoke_specialist", new() { ["task"] = "temperature?" }),
            ScriptedEvaluationAgentFactory.Answer("The living room is 21.5 degrees."));
        var request = CreateRequest(new AgentEvaluationAnswerRequest { Number = 21.5 }, repetitions: 2);

        var result = await CreateEvaluator(factory).EvaluateAsync(Root, request, TestContext.Current.CancellationToken);

        Assert.True(result.Passed);
        Assert.Equal(1d, result.PassRate);
        Assert.Equal([1, 2], result.Runs.Select(run => run.Repetition));
        Assert.All(result.Runs, run => Assert.Equal(["invoke_specialist"], run.ToolCalls));
        Assert.Contains("invoke_specialist", factory.OfferedTools);
        Assert.Contains("get_current_datetime_state", factory.OfferedTools);
    }

    [Fact]
    public async Task EvaluateAsync_FailsWhenARequiredToolIsSkippedOrTheAnswerIsWrong()
    {
        var factory = new ScriptedEvaluationAgentFactory(ScriptedEvaluationAgentFactory.Answer("It is 30 degrees."));
        var request = CreateRequest(new AgentEvaluationAnswerRequest { Number = 21.5 });

        var result = await CreateEvaluator(factory).EvaluateAsync(Root, request, TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        var run = Assert.Single(result.Runs);
        Assert.False(run.AnswerPassed);
        Assert.False(run.ToolSelectionPassed);
        Assert.Contains(run.FailureReasons, reason => reason.Contains("did not call invoke_specialist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_PassesWhenThePassRateMeetsTheMinimum()
    {
        var factory = new ScriptedEvaluationAgentFactory(ScriptedEvaluationAgentFactory.Answer("No idea."));
        var request = CreateRequest(new AgentEvaluationAnswerRequest(), requiredTools: false) with { MinimumPassRate = 0d };

        var result = await CreateEvaluator(factory).EvaluateAsync(Root, request, TestContext.Current.CancellationToken);

        Assert.True(result.Passed);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("pattern")]
    [InlineData("empty-group")]
    public void Validate_RejectsMalformedRequests(string defect)
    {
        var scenario = CreateRequest(new AgentEvaluationAnswerRequest()).Scenarios[0];
        var request = defect switch
        {
            "duplicate" => new EvaluateAgentDefinitionRequest { Scenarios = [scenario, scenario] },
            "pattern" => new EvaluateAgentDefinitionRequest
            {
                Scenarios = [scenario with { Answer = new AgentEvaluationAnswerRequest { Pattern = "(" } }],
            },
            _ => new EvaluateAgentDefinitionRequest { Scenarios = [scenario with { RequiredToolGroups = [[]] }] },
        };

        Assert.NotNull(AgentDefinitionEvaluator.Validate(request));
    }

    internal static EvaluateAgentDefinitionRequest CreateRequest(
        AgentEvaluationAnswerRequest answer,
        int repetitions = 1,
        bool requiredTools = true) => new()
        {
            Repetitions = repetitions,
            Scenarios =
            [
                new AgentEvaluationScenarioRequest
                {
                    Id = "temperature",
                    Question = "How warm is the living room?",
                    Answer = answer,
                    RequiredToolGroups = requiredTools ? [["invoke_specialist"]] : [],
                    ToolResponses = new Dictionary<string, JsonElement>
                    {
                        ["invoke_specialist"] = JsonSerializer.SerializeToElement("21.5 degrees"),
                    },
                },
            ],
        };

    private static AgentDefinitionEvaluator CreateEvaluator(IEvaluationAgentFactory factory)
    {
        var definitionStore = new StaticDefinitionStore(Specialist);
        var builtInTools = new AgentRuntimeBuiltInTools(
            NullLogger<AgentRuntimeBuiltInTools>.Instance,
            Options.Create(new AgentRuntimeConfig
            {
                Tenants = new Dictionary<string, TenantAgentConfig> { ["tenant-a"] = new() },
            }),
            TimeProvider.System,
            new StaticTenantContext(),
            definitionStore);
        return new AgentDefinitionEvaluator(
            factory,
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider(),
            definitionStore,
            builtInTools,
            new NullCredentialStore(),
            new NullCredentialStore(),
            Options.Create(new AgentDefinitionPolicyConfig()));
    }

    private sealed class StaticTenantContext : ITenantContext
    {
        public string TenantId => "tenant-a";
    }

    private sealed class StaticDefinitionStore(params AgentDefinition[] definitions) : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(definitions.FirstOrDefault(definition => definition.Name == agentName));
    }

    private sealed class NullCredentialStore : IProviderCredentialStore, IMcpCredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public Azure.Core.TokenCredential? GetTokenCredential(string providerName) => null;

        public ValueTask<string?> GetAuthorizationHeaderAsync(string credentialName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }
}
