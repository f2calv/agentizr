using System.Text.Json.Nodes;

namespace CasCap.Services;

/// <summary>Everything a realtime call controller needs after the setup request's scope has ended.</summary>
internal sealed record RealtimeCallSessionOptions
{
    /// <summary>Gets the opaque runtime call identifier.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the owning tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the agent the call belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the tenant provider name whose credential controls and hangs up the call.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the realtime resource endpoint.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the provider call identifier; it never leaves the runtime.</summary>
    public required string ProviderCallId { get; init; }

    /// <summary>Gets the runtime credential for the control connection and hang-up.</summary>
    public required RealtimeProviderCredential Credential { get; init; }

    /// <summary>Gets the session configuration reapplied when control starts.</summary>
    public required JsonObject Session { get; init; }

    /// <summary>Gets the hard ceiling on call length, counted from call start.</summary>
    public required TimeSpan MaximumDuration { get; init; }

    /// <summary>Gets call-owned evidence and reauthorization checks, never persisted with call records.</summary>
    public RealtimeKnowledgeSession? Knowledge { get; init; }

    /// <summary>Gets opt-in voice-only history, never shared with text sessions or knowledge-enabled calls.</summary>
    public RealtimeConversationLease? Conversation { get; init; }

    /// <summary>Gets the deployment's configured token prices, or <see langword="null"/> when cost is not estimated.</summary>
    public RealtimeTokenPricing? Pricing { get; init; }
}
