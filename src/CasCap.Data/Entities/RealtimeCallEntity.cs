namespace CasCap.Entities;

/// <summary>Leased ownership record of one brokered realtime call, so any replica can end it or sweep a lost owner's call.</summary>
public sealed class RealtimeCallEntity
{
    /// <summary>Gets or sets the opaque runtime call identifier.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public required string TenantId { get; set; }

    /// <summary>Gets or sets the agent the call belongs to.</summary>
    public required string AgentName { get; set; }

    /// <summary>Gets or sets the runtime instance that controls the call, or that claimed it to hang up.</summary>
    public required string OwnerId { get; set; }

    /// <summary>Gets or sets <c>Active</c>, <c>Ending</c> or <c>Ended</c>.</summary>
    public required string Status { get; set; }

    /// <summary>Gets or sets the tenant provider name whose credential can hang the call up.</summary>
    public required string ProviderName { get; set; }

    /// <summary>Gets or sets the realtime resource endpoint.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Gets or sets the provider's call identifier; it never leaves the runtime.</summary>
    public required string ProviderCallId { get; set; }

    /// <summary>Gets or sets when the runtime started controlling the call.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Gets or sets the owner's lease deadline; a call past it without completion is swept.</summary>
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets when the call ended.</summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>Gets or sets the latest token usage the owner reported.</summary>
    public string? UsageJson { get; set; }

    /// <summary>Gets or sets the end reason, duration, usage and estimated cost once ended.</summary>
    public string? SummaryJson { get; set; }
}
