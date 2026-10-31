namespace CasCap.Models;

/// <summary>Application-level delegation or compaction activity observed during one agent turn.</summary>
public sealed record AgentExecutionEvent
{
    /// <summary>Gets the stable event type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the affected agent name.</summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the delegation depth.</summary>
    public int? Depth { get; init; }

    /// <summary>Gets the model name.</summary>
    public string? ModelName { get; init; }

    /// <summary>Gets elapsed time from the parent turn start.</summary>
    public TimeSpan? Elapsed { get; init; }

    /// <summary>Gets messages before compaction.</summary>
    public int? InputMessageCount { get; init; }

    /// <summary>Gets messages after compaction.</summary>
    public int? OutputMessageCount { get; init; }

    /// <summary>Gets tool-only messages removed.</summary>
    public int? ToolMessagesDropped { get; init; }

    /// <summary>Gets messages removed by the sliding window.</summary>
    public int? WindowMessagesTrimmed { get; init; }

    /// <summary>Gets the configured target message count.</summary>
    public int? TargetMessageCount { get; init; }

    /// <summary>Gets delegated-agent diagnostics for completion events.</summary>
    public AgentRunResult? Diagnostics { get; init; }
}
