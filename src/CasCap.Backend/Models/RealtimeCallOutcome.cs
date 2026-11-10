namespace CasCap.Models;

/// <summary>Result of starting a brokered realtime call.</summary>
public sealed record RealtimeCallOutcome
{
    /// <summary>Gets the runtime call identifier when the call started.</summary>
    public string? CallId { get; init; }

    /// <summary>Gets the SDP answer when the call started.</summary>
    public string? SdpAnswer { get; init; }

    /// <summary>Gets whether this instance has no capacity for another call.</summary>
    public bool IsAtCapacity { get; init; }

    /// <summary>Gets whether the realtime provider or its credential was unavailable.</summary>
    public bool IsProviderUnavailable { get; init; }
}
