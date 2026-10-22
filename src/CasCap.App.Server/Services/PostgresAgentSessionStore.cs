namespace CasCap.Services;

/// <summary>PostgreSQL-backed tenant-qualified agent session store with Redis payload caching.</summary>
internal sealed class PostgresAgentSessionStore(
    PostgresAgentStateStore stateStore,
    ITenantContext tenantContext) : IAgentSessionStore
{
    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken)
        => stateStore.GetAsync<string>(GetKey(agentName, definitionVersion, sessionId), cancellationToken);

    /// <inheritdoc/>
    public ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string sessionStateJson,
        CancellationToken cancellationToken)
        => stateStore.WriteAsync(
            agentName, sessionId, GetKey(agentName, definitionVersion, sessionId), sessionStateJson, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DeleteAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken)
        => stateStore.WriteAsync<string>(
            agentName, sessionId, GetKey(agentName, definitionVersion, sessionId), null, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<string?> GetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
        => stateStore.GetAsync<string>(
            GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName), cancellationToken);

    /// <inheritdoc/>
    public ValueTask SetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        string sessionStateJson,
        CancellationToken cancellationToken)
        => stateStore.WriteAsync(
            agentName, sessionId, GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName),
            sessionStateJson, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DeleteSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken)
        => stateStore.WriteAsync<string>(
            agentName, sessionId, GetSnapshotKey(agentName, definitionVersion, sessionId, snapshotName),
            null, cancellationToken);

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
