using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>Tracks the realtime calls this instance controls and ends them when the host stops.</summary>
/// <remarks>
/// TODO: persist a leased call record so any replica can end a call and an expired owner's calls are swept,
/// as ADR 0002 requires before running more than one runtime replica with realtime calls.
/// </remarks>
internal sealed class RealtimeCallRegistry(IOptions<AgentRuntimeConfig> runtimeConfig) : IHostedService
{
    private readonly ConcurrentDictionary<string, RealtimeCallSession> _calls = new(StringComparer.Ordinal);
    private readonly Lock _admission = new();

    /// <summary>Adds a call unless this instance is at capacity, and removes it once it ends.</summary>
    public bool TryAdd(RealtimeCallSession call)
    {
        lock (_admission)
        {
            if (_calls.Count >= runtimeConfig.Value.MaximumRealtimeCallsPerInstance || !_calls.TryAdd(call.CallId, call))
                return false;
        }
        call.Ended += (_, _) => _calls.TryRemove(call.CallId, out _);
        return true;
    }

    /// <summary>Gets whether a new call could currently be admitted.</summary>
    public bool HasCapacity => _calls.Count < runtimeConfig.Value.MaximumRealtimeCallsPerInstance;

    /// <summary>Finds a call owned by the tenant and agent.</summary>
    public RealtimeCallSession? Find(string tenantId, string agentName, string callId) =>
        _calls.TryGetValue(callId, out var call)
            && call.TenantId.Equals(tenantId, StringComparison.Ordinal)
            && call.AgentName.Equals(agentName, StringComparison.Ordinal)
                ? call
                : null;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken) =>
        await Task.WhenAll(_calls.Values.Select(call => call.DisposeAsync().AsTask())).WaitAsync(cancellationToken);
}
