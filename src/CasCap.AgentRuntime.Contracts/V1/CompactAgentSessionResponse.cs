namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports the outcome of an active-session compaction request.</summary>
public sealed record CompactAgentSessionResponse
{
    /// <summary>Gets whether active session state existed.</summary>
    /// <example>true</example>
    public bool SessionExists { get; init; }

    /// <summary>Gets whether the session exposed compactable chat history.</summary>
    /// <example>true</example>
    public bool HistoryAvailable { get; init; }

    /// <summary>Gets the number of messages removed.</summary>
    /// <example>14</example>
    public int RemovedMessageCount { get; init; }
}
