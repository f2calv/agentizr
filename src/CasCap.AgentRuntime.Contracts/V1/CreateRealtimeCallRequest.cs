namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Starts a brokered realtime voice call from a WebRTC SDP offer.</summary>
public sealed record CreateRealtimeCallRequest
{
    /// <summary>Gets the client's SDP offer.</summary>
    [Required, MinLength(1), MaxLength(65_536)]
    public required string SdpOffer { get; init; }

    /// <summary>Gets whether completed voice transcripts may be retained for twenty-four hours for resumption.</summary>
    /// <example>false</example>
    public bool RetainConversation { get; init; }

    /// <summary>Gets a previously issued conversation reference to resume, never a text-session identifier.</summary>
    /// <example>00000000-0000-0000-0000-000000000001</example>
    public Guid? ConversationId { get; init; }
}
