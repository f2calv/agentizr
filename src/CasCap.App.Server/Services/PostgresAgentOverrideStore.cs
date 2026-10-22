namespace CasCap.Services;

/// <summary>PostgreSQL-backed tenant-qualified runtime overrides with Redis payload caching.</summary>
internal sealed class PostgresAgentOverrideStore(
    PostgresAgentStateStore stateStore,
    ITenantContext tenantContext) : IAgentOverrideStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentOverrideState> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await stateStore.GetAsync<AgentOverrideState>(
            GetKey(agentName, definitionVersion, sessionId), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return value ?? new AgentOverrideState();
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        AgentOverrideState state,
        CancellationToken cancellationToken)
        => stateStore.WriteAsync(
            agentName, sessionId, GetKey(agentName, definitionVersion, sessionId), state, cancellationToken);

    private string GetKey(string agentName, string definitionVersion, string sessionId) =>
        AgentStateKey.CreateRedis("override", tenantContext.TenantId, agentName, definitionVersion, sessionId);
}
