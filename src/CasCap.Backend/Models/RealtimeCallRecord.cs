using CasCap.AgentRuntime.Contracts.V1;

namespace CasCap.Models;

/// <summary>Lifecycle of a leased realtime call record.</summary>
internal enum RealtimeCallStatus
{
    /// <summary>The owner controls the call.</summary>
    Active,

    /// <summary>An end was requested; the owner, or a sweeper after the lease expires, hangs it up.</summary>
    Ending,

    /// <summary>The call ended and its summary is recorded.</summary>
    Ended,
}

/// <summary>A realtime call's shared ownership record.</summary>
internal sealed record RealtimeCallRecord
{
    /// <summary>Gets the opaque runtime call identifier.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the owning tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the agent the call belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the instance that controls, or claimed, the call.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Gets the lifecycle status.</summary>
    public required RealtimeCallStatus Status { get; init; }

    /// <summary>Gets the tenant provider name whose credential can hang the call up.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the realtime resource endpoint.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the provider's call identifier; it never leaves the runtime.</summary>
    public required string ProviderCallId { get; init; }

    /// <summary>Gets when the runtime started controlling the call.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the owner's lease deadline.</summary>
    public required DateTimeOffset LeaseExpiresAt { get; init; }

    /// <summary>Gets the latest usage the owner reported, if any.</summary>
    public RealtimeCallUsage? Usage { get; init; }

    /// <summary>Gets the summary once the call ended.</summary>
    public RealtimeCallSummaryResponse? Summary { get; init; }
}
