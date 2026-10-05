namespace CasCap.Services;

/// <summary>Process-local tenant-qualified override store used by the initial runtime slice.</summary>
internal sealed class InMemoryAgentOverrideStore(
    AgentRuntimeState state,
    ITenantContext tenantContext) : IAgentOverrideStore
{
    /// <inheritdoc/>
    public ValueTask<AgentOverrideState> GetAsync(string agentName, string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stateValue = state.Overrides.GetValueOrDefault(GetKey(agentName, sessionId)) ?? new AgentOverrideState();
        return ValueTask.FromResult(stateValue);
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(
        string agentName,
        string sessionId,
        AgentOverrideState stateValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Overrides[GetKey(agentName, sessionId)] = stateValue;
        return ValueTask.CompletedTask;
    }

    private string GetKey(string agentName, string sessionId) =>
        AgentStateKey.Create(tenantContext.TenantId, agentName, sessionId);
}
