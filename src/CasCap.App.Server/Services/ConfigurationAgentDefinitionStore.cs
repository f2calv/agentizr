namespace CasCap.Services;

/// <summary>Resolves tenant agent definitions from validated application configuration.</summary>
internal sealed class ConfigurationAgentDefinitionStore(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext) : IAgentDefinitionStore
{
    /// <inheritdoc/>
    public ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            || !tenant.Agents.TryGetValue(agentName, out var agent)
            || !tenant.Providers.TryGetValue(agent.Provider, out var provider))
            return ValueTask.FromResult<AgentDefinition?>(null);

        return ValueTask.FromResult<AgentDefinition?>(new AgentDefinition
        {
            Name = agentName,
            Version = tenant.DefinitionVersion,
            Agent = agent,
            Provider = provider with { ApiKey = null },
        });
    }

    /// <inheritdoc/>
    public ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException(
            "Configuration-backed definitions are read-only. Configure PostgreSQL to publish definitions."));

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return await GetAsync(agentName, cancellationToken) is { } definition
            ? [new AgentDefinitionHistoryItem
            {
                DefinitionVersion = definition.Version,
                SchemaVersion = 1,
            }]
            : [];
    }
}
