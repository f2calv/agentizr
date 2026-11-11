using CasCap.AgentRuntime.Contracts.V1;

namespace CasCap.Abstractions;

/// <summary>
/// Shared, leased record of brokered realtime calls so any runtime replica can end a call, learn how it ended and
/// sweep calls whose owner was lost.
/// </summary>
internal interface IRealtimeCallStore
{
    /// <summary>Records a call this instance has started controlling.</summary>
    public Task AddAsync(RealtimeCallRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Extends the owner's lease and records its latest usage; returns <see langword="false"/> when an end was
    /// requested elsewhere or the owner no longer holds the call.
    /// </summary>
    public Task<bool> RenewAsync(
        string callId, string ownerId, DateTimeOffset leaseExpiresAt, RealtimeCallUsage usage, CancellationToken cancellationToken);

    /// <summary>Marks a call ended with its summary.</summary>
    public Task CompleteAsync(
        string callId, RealtimeCallSummaryResponse summary, DateTimeOffset endedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an active call owned by the tenant and agent as ending and returns its record, or <see langword="null"/>
    /// for an unknown or foreign call.
    /// </summary>
    public Task<RealtimeCallRecord?> RequestEndAsync(string tenantId, string agentName, string callId, CancellationToken cancellationToken);

    /// <summary>Gets a call owned by the tenant and agent, or <see langword="null"/>.</summary>
    public Task<RealtimeCallRecord?> GetAsync(string tenantId, string agentName, string callId, CancellationToken cancellationToken);

    /// <summary>Takes over unfinished calls whose lease expired, so exactly one replica hangs each one up.</summary>
    public Task<IReadOnlyList<RealtimeCallRecord>> ClaimExpiredAsync(
        string claimantId, DateTimeOffset now, DateTimeOffset claimExpiresAt, CancellationToken cancellationToken);

    /// <summary>Deletes records of calls that ended before the cutoff.</summary>
    public Task DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
