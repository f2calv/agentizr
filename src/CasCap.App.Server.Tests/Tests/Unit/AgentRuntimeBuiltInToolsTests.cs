using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Unit;

/// <summary>Verifies runtime-owned tools supplied to top-level agents.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentRuntimeBuiltInToolsTests
{
    [Fact]
    public async Task CreateAsync_AddsBuiltInsForReachableDefinitionGraph()
    {
        var specialist = CreateDefinition("specialist", "provider-b", []);
        var root = CreateDefinition("root", "provider-a", [new ToolSource { Agent = specialist.Name }]);
        var service = new AgentRuntimeBuiltInTools(
            NullLogger<AgentRuntimeBuiltInTools>.Instance,
            Options.Create(new AgentRuntimeConfig
            {
                Tenants = new Dictionary<string, TenantAgentConfig>
                {
                    ["tenant-a"] = new TenantAgentConfig { TimeZoneId = "UTC" },
                },
            }),
            TimeProvider.System,
            new StaticTenantContext(),
            new StaticDefinitionStore(specialist));

        var tools = await service.CreateAsync(root, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["get_agents", "get_current_datetime_state", "get_providers"],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    private static AgentDefinition CreateDefinition(
        string name,
        string providerName,
        ToolSource[] tools) =>
        new()
        {
            Name = name,
            Version = "1",
            Agent = new AgentConfig
            {
                Name = name,
                Description = $"{name} description",
                Prompt = $"{name} prompt",
                Provider = providerName,
                Tools = tools,
            },
            Provider = new ProviderConfig
            {
                Type = AgentType.OpenAI,
                ModelName = $"{providerName}-model",
            },
        };

    private sealed class StaticTenantContext : ITenantContext
    {
        public string TenantId => "tenant-a";
    }

    private sealed class StaticDefinitionStore(params AgentDefinition[] definitions) : IAgentDefinitionStore
    {
        private readonly IReadOnlyDictionary<string, AgentDefinition> _definitions =
            definitions.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_definitions.GetValueOrDefault(agentName));
    }
}
