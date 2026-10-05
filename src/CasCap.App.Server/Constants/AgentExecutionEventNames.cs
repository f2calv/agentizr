namespace CasCap.Constants;

/// <summary>Stable wire names for Agent Runtime execution events.</summary>
public static class AgentExecutionEventNames
{
    /// <summary>A sub-agent delegation started.</summary>
    public const string DelegationStarted = "delegation.started";

    /// <summary>A sub-agent delegation completed.</summary>
    public const string DelegationCompleted = "delegation.completed";

    /// <summary>Session history was compacted.</summary>
    public const string SessionCompacted = "session.compacted";
}
