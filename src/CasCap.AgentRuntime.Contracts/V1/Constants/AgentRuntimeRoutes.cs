namespace CasCap.AgentRuntime.Contracts.V1.Constants;

/// <summary>Version 1 Agent Runtime route templates.</summary>
public static class AgentRuntimeRoutes
{
    /// <summary>Gets the tenant-scoped agent route group.</summary>
    public const string AgentGroup = "/api/v1/agents/{agentName}";

    /// <summary>Gets the route for running an agent turn.</summary>
    public const string Runs = "/runs";

    /// <summary>Gets the route for active session inspection and reset.</summary>
    public const string Session = "/sessions/{sessionId}";

    /// <summary>Gets the route for active session compaction.</summary>
    public const string SessionCompaction = "/sessions/{sessionId}/compact";

    /// <summary>Gets the route for a named session snapshot.</summary>
    public const string SessionSnapshot = "/sessions/{sessionId}/snapshots/{snapshotName}";

    /// <summary>Gets the route for activating a named session snapshot.</summary>
    public const string SessionSnapshotActivation = "/sessions/{sessionId}/snapshots/{snapshotName}/activate";

    /// <summary>Gets the route for complete per-session runtime overrides.</summary>
    public const string SessionOverrides = "/sessions/{sessionId}/overrides";
}
