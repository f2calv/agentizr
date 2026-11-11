namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Why a brokered realtime voice call ended.</summary>
public enum RealtimeCallEndReason
{
    /// <summary>The client ended the call.</summary>
    ClientRequested,

    /// <summary>The call reached its configured hard ceiling, counted from call start.</summary>
    MaximumDuration,

    /// <summary>The provider closed the call's control connection, for example after the client disconnected.</summary>
    ProviderClosed,

    /// <summary>The runtime lost control of the call because of a connection or protocol failure.</summary>
    ControlLost,

    /// <summary>The runtime instance controlling the call shut down.</summary>
    HostShutdown,
}
