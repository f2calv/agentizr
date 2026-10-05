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
            Version = tenant.DefinitionVersion,
            Agent = agent,
            Provider = provider with { ApiKey = null },
        });
    }
}
