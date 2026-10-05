namespace CasCap.Services;

/// <summary>Redis-backed tenant-qualified agent session store.</summary>
internal sealed class RedisAgentSessionStore(
    IRemoteCache remoteCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext) : IAgentSessionStore
{
    /// <inheritdoc/>
    public async ValueTask<string?> GetAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await remoteCache.GetAsync(GetKey(agentName, sessionId));
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(
        string agentName,
        string sessionId,
        string sessionStateJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await remoteCache.SetAsync(
            GetKey(agentName, sessionId),
            sessionStateJson,
            TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours));
        if (!stored)
            throw new InvalidOperationException("Agent session state could not be persisted.");
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await remoteCache.DeleteAsync(GetKey(agentName, sessionId));
    }

    private string GetKey(string agentName, string sessionId) =>
        AgentStateKey.CreateRedis("session", tenantContext.TenantId, agentName, sessionId);
}
