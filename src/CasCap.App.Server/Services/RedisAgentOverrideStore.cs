namespace CasCap.Services;

/// <summary>Redis-backed tenant-qualified runtime override store.</summary>
internal sealed class RedisAgentOverrideStore(
    IRemoteCache remoteCache,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext) : IAgentOverrideStore
{
    /// <inheritdoc/>
    public async ValueTask<AgentOverrideState> GetAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await remoteCache.GetAsync(GetKey(agentName, sessionId));
        cancellationToken.ThrowIfCancellationRequested();
        return value is null
            ? new AgentOverrideState()
            : JsonSerializer.Deserialize<AgentOverrideState>(value, JsonSerializerOptions.Web)
                ?? throw new InvalidDataException("Agent override state could not be deserialized.");
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(
        string agentName,
        string sessionId,
        AgentOverrideState stateValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await remoteCache.SetAsync(
            GetKey(agentName, sessionId),
            JsonSerializer.Serialize(stateValue, JsonSerializerOptions.Web),
            TimeSpan.FromHours(runtimeConfig.Value.StateSlidingExpirationHours));
        if (!stored)
            throw new InvalidOperationException("Agent override state could not be persisted.");
    }

    private string GetKey(string agentName, string sessionId) =>
        AgentStateKey.CreateRedis("override", tenantContext.TenantId, agentName, sessionId);
}
