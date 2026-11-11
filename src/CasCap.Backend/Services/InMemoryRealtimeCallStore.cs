using CasCap.AgentRuntime.Contracts.V1;
using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>Process-local realtime call records for a single instance without PostgreSQL.</summary>
internal sealed class InMemoryRealtimeCallStore : IRealtimeCallStore
{
    private readonly ConcurrentDictionary<string, RealtimeCallRecord> _calls = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc/>
    public Task AddAsync(RealtimeCallRecord record, CancellationToken cancellationToken)
    {
        _calls[record.CallId] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> RenewAsync(
        string callId, string ownerId, DateTimeOffset leaseExpiresAt, RealtimeCallUsage usage, CancellationToken cancellationToken) =>
        Task.FromResult(Update(callId,
            call => call.OwnerId == ownerId && call.Status == RealtimeCallStatus.Active,
            call => call with { LeaseExpiresAt = leaseExpiresAt, Usage = usage }));

    /// <inheritdoc/>
    public Task CompleteAsync(
        string callId, RealtimeCallSummaryResponse summary, DateTimeOffset endedAt, CancellationToken cancellationToken)
    {
        Update(callId,
            call => call.Status != RealtimeCallStatus.Ended,
            call => call with { Status = RealtimeCallStatus.Ended, Summary = summary, LeaseExpiresAt = endedAt });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<RealtimeCallRecord?> RequestEndAsync(
        string tenantId, string agentName, string callId, CancellationToken cancellationToken)
    {
        Update(callId,
            call => IsOwner(call, tenantId, agentName) && call.Status == RealtimeCallStatus.Active,
            call => call with { Status = RealtimeCallStatus.Ending });
        return GetAsync(tenantId, agentName, callId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<RealtimeCallRecord?> GetAsync(string tenantId, string agentName, string callId, CancellationToken cancellationToken) =>
        Task.FromResult(_calls.TryGetValue(callId, out var call) && IsOwner(call, tenantId, agentName) ? call : null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<RealtimeCallRecord>> ClaimExpiredAsync(
        string claimantId, DateTimeOffset now, DateTimeOffset claimExpiresAt, CancellationToken cancellationToken)
    {
        var claimed = new List<RealtimeCallRecord>();
        foreach (var callId in _calls.Keys)
        {
            if (Update(callId,
                    call => call.Status != RealtimeCallStatus.Ended && call.LeaseExpiresAt < now,
                    call => call with { OwnerId = claimantId, LeaseExpiresAt = claimExpiresAt }))
                claimed.Add(_calls[callId]);
        }
        return Task.FromResult<IReadOnlyList<RealtimeCallRecord>>(claimed);
    }

    /// <inheritdoc/>
    public Task DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        foreach (var (callId, call) in _calls)
        {
            // An ended record's lease deadline holds its end time.
            if (call.Status == RealtimeCallStatus.Ended && call.LeaseExpiresAt < cutoff)
                _calls.TryRemove(callId, out _);
        }
        return Task.CompletedTask;
    }

    private static bool IsOwner(RealtimeCallRecord call, string tenantId, string agentName) =>
        call.TenantId.Equals(tenantId, StringComparison.Ordinal) && call.AgentName.Equals(agentName, StringComparison.Ordinal);

    private bool Update(string callId, Func<RealtimeCallRecord, bool> condition, Func<RealtimeCallRecord, RealtimeCallRecord> change)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(callId, out var call) || !condition(call))
                return false;
            _calls[callId] = change(call);
            return true;
        }
    }
}
