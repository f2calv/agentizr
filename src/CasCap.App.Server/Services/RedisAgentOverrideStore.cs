namespace CasCap.Services;

/// <summary>Redis-backed tenant-qualified runtime override store.</summary>
internal sealed class RedisAgentOverrideStore(
    IDistributedCache distributedCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
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
        var value = await distributedCache.Get<AgentOverrideState>(GetKey(agentName, definitionVersion, sessionId));
        cancellationToken.ThrowIfCancellationRequested();
        return value ?? new AgentOverrideState();
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        AgentOverrideState stateValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await distributedCache.Set(
            GetKey(agentName, definitionVersion, sessionId),
            stateValue,
            TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours));
    }

    private string GetKey(string agentName, string definitionVersion, string sessionId) =>
        AgentStateKey.CreateRedis("override", tenantContext.TenantId, agentName, definitionVersion, sessionId);
}
