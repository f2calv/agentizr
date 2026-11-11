using CasCap.AgentRuntime.Contracts.V1;
using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>
/// Tracks the realtime calls this instance controls, keeps recently ended calls' summaries for their clients and
/// ends active calls when the host stops.
/// </summary>
/// <remarks>
/// TODO: persist a leased call record so any replica can end a call and an expired owner's calls are swept,
/// as ADR 0002 requires before running more than one runtime replica with realtime calls.
/// </remarks>
internal sealed class RealtimeCallRegistry(IOptions<AgentRuntimeConfig> runtimeConfig, TimeProvider timeProvider) : IHostedService
{
    // Long enough for a client to learn why the runtime ended a call after its media dropped.
    private static readonly TimeSpan EndedSummaryRetention = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, RealtimeCallSession> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EndedCall> _ended = new(StringComparer.Ordinal);
    private readonly Lock _admission = new();

    /// <summary>Adds a call unless this instance is at capacity, and retains its summary once it ends.</summary>
    public bool TryAdd(RealtimeCallSession call)
    {
        lock (_admission)
        {
            if (_calls.Count >= runtimeConfig.Value.MaximumRealtimeCallsPerInstance || !_calls.TryAdd(call.CallId, call))
                return false;
        }
        call.Ended += (_, _) =>
        {
            _ended[call.CallId] = new EndedCall(
                call.TenantId, call.AgentName, call.Summary!, timeProvider.GetUtcNow() + EndedSummaryRetention);
            _calls.TryRemove(call.CallId, out _);
        };
        return true;
    }

    /// <summary>Gets whether a new call could currently be admitted.</summary>
    public bool HasCapacity => _calls.Count < runtimeConfig.Value.MaximumRealtimeCallsPerInstance;

    /// <summary>
    /// Ends an active call owned by the tenant and agent, or returns the retained summary of one that already ended;
    /// returns <see langword="null"/> for an unknown, foreign or expired call.
    /// </summary>
    public async Task<RealtimeCallSummaryResponse?> EndAsync(string tenantId, string agentName, string callId)
    {
        RemoveExpired();
        if (_calls.TryGetValue(callId, out var call) && IsOwner(call.TenantId, call.AgentName, tenantId, agentName))
            return await call.EndAsync(RealtimeCallEndReason.ClientRequested);
        return _ended.TryGetValue(callId, out var ended) && IsOwner(ended.TenantId, ended.AgentName, tenantId, agentName)
            ? ended.Summary
            : null;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken) =>
        await Task.WhenAll(_calls.Values.Select(call => call.DisposeAsync().AsTask())).WaitAsync(cancellationToken);

    private static bool IsOwner(string callTenantId, string callAgentName, string tenantId, string agentName) =>
        callTenantId.Equals(tenantId, StringComparison.Ordinal) && callAgentName.Equals(agentName, StringComparison.Ordinal);

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (callId, ended) in _ended)
        {
            if (ended.ExpiresAt <= now)
                _ended.TryRemove(callId, out _);
        }
    }

    private sealed record EndedCall(string TenantId, string AgentName, RealtimeCallSummaryResponse Summary, DateTimeOffset ExpiresAt);
}
