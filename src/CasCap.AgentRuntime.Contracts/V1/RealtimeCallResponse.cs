namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>The SDP answer for a brokered realtime voice call and the runtime's identifier for ending it.</summary>
/// <remarks>The provider's own call identifier and ephemeral credentials never leave the runtime.</remarks>
public sealed record RealtimeCallResponse
{
    /// <summary>Gets the opaque runtime call identifier used to end the call.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the SDP answer to apply as the client's remote description.</summary>
    public required string SdpAnswer { get; init; }
}
