namespace CasCap.Services;

/// <summary>Qualifies Development session ownership with the current tenant.</summary>
internal sealed class ConfiguredAgentSessionLock(
    InMemoryAgentSessionLock sessionLock,
    ITenantContext tenantContext) : IAgentSessionLock
{
    /// <inheritdoc/>
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken) =>
        sessionLock.AcquireAsync(tenantContext.TenantId, agentName, sessionId, cancellationToken);
}
