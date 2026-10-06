namespace CasCap.Services;

/// <summary>Redis-backed tenant-qualified agent session store.</summary>
internal sealed class RedisAgentSessionStore(
    IDistributedCache distributedCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext) : IAgentSessionStore
{
    /// <inheritdoc/>
    public async ValueTask<string?> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await distributedCache.Get<string>(GetKey(agentName, definitionVersion, sessionId));
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string sessionStateJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await distributedCache.Set(
            GetKey(agentName, definitionVersion, sessionId),
            sessionStateJson,
            TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours));
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await distributedCache.Delete(GetKey(agentName, definitionVersion, sessionId));
    }

    private string GetKey(string agentName, string definitionVersion, string sessionId) =>
        AgentStateKey.CreateRedis("session", tenantContext.TenantId, agentName, definitionVersion, sessionId);
}
