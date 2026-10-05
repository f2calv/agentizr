namespace CasCap.Services;

/// <summary>Process-local tenant-qualified session store used by the initial runtime slice.</summary>
internal sealed class InMemoryAgentSessionStore(
    AgentRuntimeState state,
    ITenantContext tenantContext) : IAgentSessionStore
{
    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(string agentName, string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(state.Sessions.GetValueOrDefault(GetKey(agentName, sessionId)));
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(string agentName, string sessionId, string sessionStateJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Sessions[GetKey(agentName, sessionId)] = sessionStateJson;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(string agentName, string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Sessions.TryRemove(GetKey(agentName, sessionId), out _);
        return ValueTask.CompletedTask;
    }

    private string GetKey(string agentName, string sessionId) =>
        AgentStateKey.Create(tenantContext.TenantId, agentName, sessionId);
}
