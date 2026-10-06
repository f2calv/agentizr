namespace CasCap.Models;

/// <summary>Result of compacting serialized Agent Framework session state.</summary>
public sealed record AgentSessionCompactionResult
{
    /// <summary>Gets whether compactable chat history was present.</summary>
    public bool HistoryAvailable { get; init; }

    /// <summary>Gets the number of messages removed.</summary>
    public int RemovedMessageCount { get; init; }

    /// <summary>Gets the resulting serialized session state.</summary>
    public required string SessionStateJson { get; init; }
}
