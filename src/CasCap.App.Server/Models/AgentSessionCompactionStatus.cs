namespace CasCap.Models;

/// <summary>Resolved outcome of compacting one tenant-scoped agent session.</summary>
public sealed record AgentSessionCompactionStatus
{
    /// <summary>Gets whether the requested agent definition exists.</summary>
    public bool AgentExists { get; init; }

    /// <summary>Gets whether active serialized session state exists.</summary>
    public bool SessionExists { get; init; }

    /// <summary>Gets whether compactable chat history was present.</summary>
    public bool HistoryAvailable { get; init; }

    /// <summary>Gets the number of messages removed.</summary>
    public int RemovedMessageCount { get; init; }
}
