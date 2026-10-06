namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Describes delegation or compaction activity observed during an agent turn.</summary>
public sealed record RunAgentEvent
{
    /// <summary>Gets the stable event type.</summary>
    /// <example>delegation.started</example>
    public required string Type { get; init; }

    /// <summary>Gets the affected agent name for delegation events.</summary>
    /// <example>SecurityAgent</example>
    public string? AgentName { get; init; }

    /// <summary>Gets the delegation nesting depth.</summary>
    /// <example>1</example>
    public int? Depth { get; init; }

    /// <summary>Gets the model name for delegation events.</summary>
    /// <example>qwen3:32b</example>
    public string? ModelName { get; init; }

    /// <summary>Gets the elapsed milliseconds from the start of the parent turn.</summary>
    /// <example>1250</example>
    public double? ElapsedMilliseconds { get; init; }

    /// <summary>Gets the message count before compaction.</summary>
    public int? InputMessageCount { get; init; }

    /// <summary>Gets the message count after compaction.</summary>
    public int? OutputMessageCount { get; init; }

    /// <summary>Gets the number of tool-only messages removed.</summary>
    public int? ToolMessagesDropped { get; init; }

    /// <summary>Gets the number of messages removed by the sliding window.</summary>
    public int? WindowMessagesTrimmed { get; init; }

    /// <summary>Gets the configured target message count.</summary>
    public int? TargetMessageCount { get; init; }

    /// <summary>Gets delegated-agent diagnostics for completion events.</summary>
    public RunAgentStepResult? Result { get; init; }
}
