namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Starts a brokered realtime voice call from a WebRTC SDP offer.</summary>
public sealed record CreateRealtimeCallRequest
{
    /// <summary>Gets the client's SDP offer.</summary>
    [Required, MinLength(1), MaxLength(65_536)]
    public required string SdpOffer { get; init; }
}
