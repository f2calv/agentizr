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

    /// <inheritdoc/>
    public async ValueTask<string?> GetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await distributedCache.Get<string>(GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName));
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    /// <inheritdoc/>
    public async ValueTask SetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        string sessionStateJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await distributedCache.Set(
            GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName),
            sessionStateJson,
            TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours));
    }

    /// <inheritdoc/>
    public async ValueTask DeleteSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await distributedCache.Delete(GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName));
    }

    private string GetKey(string agentName, string definitionVersion, string sessionId) =>
        AgentStateKey.CreateRedis("session", tenantContext.TenantId, agentName, definitionVersion, sessionId);

    private string GetSnapshotKey(string agentName, string definitionVersion, string sessionId, string snapshotName) =>
        AgentStateKey.CreateRedisSnapshot(
            "session-snapshot",
            tenantContext.TenantId,
            agentName,
            definitionVersion,
            sessionId,
            snapshotName);
}
