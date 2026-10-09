using Microsoft.Extensions.AI;

namespace CasCap.Services;

/// <summary>Creates tenant-scoped tools supplied by the runtime to every top-level agent run.</summary>
internal sealed class AgentRuntimeBuiltInTools(
    ILogger<AgentRuntimeBuiltInTools> logger,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    TimeProvider timeProvider,
    ITenantContext tenantContext,
    IAgentDefinitionStore definitionStore)
{
    /// <summary>Creates built-in tools over the root agent's reachable active definition graph.</summary>
    public async ValueTask<IReadOnlyList<AITool>> CreateAsync(
        AgentDefinition rootDefinition,
        CancellationToken cancellationToken)
    {
        var definitions = await GetReachableDefinitionsAsync(rootDefinition, cancellationToken);
        return
        [
            AIFunctionFactory.Create(
                (Func<AgentRuntimeDateTimeState>)GetCurrentDatetimeState,
                new AIFunctionFactoryOptions
                {
                    Name = "get_current_datetime_state",
                    Description = "Gets the current local date, time, day of week and UTC offset for this tenant.",
                }),
            AIFunctionFactory.Create(
                (Func<ProviderInfo[]>)(() => CreateProviderInfo(definitions)),
                new AIFunctionFactoryOptions
                {
                    Name = "get_providers",
                    Description = "Lists providers used by this agent and its reachable delegated agents.",
                }),
            AIFunctionFactory.Create(
                (Func<AgentInfo[]>)(() => CreateAgentInfo(definitions)),
                new AIFunctionFactoryOptions
                {
                    Name = "get_agents",
                    Description = "Lists this agent and its reachable delegated agents with capability metadata.",
                }),
        ];
    }

    private AgentRuntimeDateTimeState GetCurrentDatetimeState()
    {
        var timeZoneId = runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            ? tenant.TimeZoneId
            : TimeZoneInfo.Utc.Id;
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                ex,
                "Tenant time zone {TimeZoneId} is unavailable; using UTC",
                timeZoneId);
            timeZone = TimeZoneInfo.Utc;
        }

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, timeZone);
        return new AgentRuntimeDateTimeState
        {
            LocalTime = localTime,
            DayOfWeek = localTime.DayOfWeek.ToString(),
            UtcOffset = timeZone.GetUtcOffset(localTime).ToString(),
            TimeZone = timeZone.Id,
        };
    }

    private async ValueTask<IReadOnlyDictionary<string, AgentDefinition>> GetReachableDefinitionsAsync(
        AgentDefinition rootDefinition,
        CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [rootDefinition.Name] = rootDefinition,
        };
        var pending = new Queue<AgentDefinition>();
        pending.Enqueue(rootDefinition);

        while (pending.TryDequeue(out var definition))
        {
            foreach (var delegatedAgent in definition.Agent.Tools
                .Select(tool => tool.Agent)
                .Where(agentName => !string.IsNullOrWhiteSpace(agentName)))
            {
                if (definitions.ContainsKey(delegatedAgent!))
                    continue;
                var delegatedDefinition = await definitionStore.GetAsync(delegatedAgent!, cancellationToken);
                if (delegatedDefinition is null)
                    continue;
                definitions.Add(delegatedDefinition.Name, delegatedDefinition);
                pending.Enqueue(delegatedDefinition);
            }
        }

        return definitions;
    }

    private static AgentInfo[] CreateAgentInfo(IReadOnlyDictionary<string, AgentDefinition> definitions) =>
        definitions.Values
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .Select(definition => new AgentInfo
            {
                Key = definition.Name,
                Name = definition.Agent.Name,
                Description = definition.Agent.Description,
                Enabled = definition.Agent.Enabled,
                Provider = definition.Agent.Provider,
                MaxMessages = definition.Agent.MaxMessages,
                ToolSourceCount = definition.Agent.Tools.Length,
                DelegatedAgents = definition.Agent.Tools
                    .Select(tool => tool.Agent)
                    .Where(agentName => !string.IsNullOrWhiteSpace(agentName))
                    .Select(agentName => agentName!)
                    .ToArray(),
            })
            .ToArray();

    private static ProviderInfo[] CreateProviderInfo(IReadOnlyDictionary<string, AgentDefinition> definitions) =>
        definitions.Values
            .GroupBy(definition => definition.Agent.Provider, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var definition = group.First();
                return new ProviderInfo
                {
                    Key = group.Key,
                    Type = definition.Provider.Type.ToString(),
                    ModelName = definition.Provider.ModelName,
                    Endpoint = definition.Provider.Endpoint?.ToString(),
                    ReasoningEffort = definition.Provider.ReasoningEffort?.ToString(),
                };
            })
            .ToArray();
}
