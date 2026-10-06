namespace CasCap.AgentRuntime.Contracts.V1.Constants;

/// <summary>Stable wire names for streamed Agent Runtime execution events.</summary>
public static class RunAgentEventTypes
{
    /// <summary>A delegated agent started.</summary>
    public const string DelegationStarted = "delegation.started";

    /// <summary>A delegated agent completed.</summary>
    public const string DelegationCompleted = "delegation.completed";

    /// <summary>Session history was compacted.</summary>
    public const string SessionCompacted = "session.compacted";
}
