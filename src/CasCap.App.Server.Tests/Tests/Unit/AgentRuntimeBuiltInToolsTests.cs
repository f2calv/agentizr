using Microsoft.Extensions.Logging.Abstractions;

namespace CasCap.Tests.Unit;

/// <summary>Verifies runtime-owned tools supplied to top-level agents.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentRuntimeBuiltInToolsTests
{
    [Fact]
    public async Task CreateAsync_AddsBuiltInsForReachableDefinitionGraph()
    {
        var specialist = AgentDefinitionTestData.CreateDefinition("specialist", "provider-b");
        var root = AgentDefinitionTestData.CreateDefinition(
            "root",
            "provider-a",
            [new ToolSource { Agent = specialist.Name }]);
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

    [Theory]
    [InlineData("UTC", "[Current local time: Monday 2026-10-19 06:15 (UTC, UTC+00:00)]")]
    [InlineData("Europe/Berlin", "[Current local time: Monday 2026-10-19 08:15 (Europe/Berlin, UTC+02:00)]")]
    [InlineData("America/New_York", "[Current local time: Monday 2026-10-19 02:15 (America/New_York, UTC-04:00)]")]
    public void CreateCurrentTimeContext_FormatsTenantLocalTime(string timeZoneId, string expected)
    {
        var service = CreateService(new TenantAgentConfig { TimeZoneId = timeZoneId });

        Assert.Equal(expected, service.CreateCurrentTimeContext());
    }

    [Fact]
    public void CreateCurrentTimeContext_ReturnsNullWhenDisabled()
    {
        var service = CreateService(new TenantAgentConfig { CurrentTimeContextEnabled = false });

        Assert.Null(service.CreateCurrentTimeContext());
    }

    private static AgentRuntimeBuiltInTools CreateService(TenantAgentConfig tenant) =>
        new(
            NullLogger<AgentRuntimeBuiltInTools>.Instance,
            Options.Create(new AgentRuntimeConfig
            {
                Tenants = new Dictionary<string, TenantAgentConfig> { ["tenant-a"] = tenant },
            }),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 19, 6, 15, 0, TimeSpan.Zero)),
            new StaticTenantContext(),
            new StaticDefinitionStore());

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

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
