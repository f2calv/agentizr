namespace CasCap.Models;

/// <summary>Output from one agent execution and its optional updated session state.</summary>
public sealed record AgentExecutionResult
{
    /// <summary>Gets the agent's output text.</summary>
    public required string OutputText { get; init; }

    /// <summary>Gets the version of the resolved tenant definition.</summary>
    public string DefinitionVersion { get; init; } = string.Empty;

    /// <summary>Gets the provider model used for the turn.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Gets serialized updated session state, or <see langword="null" /> when no state should be persisted.</summary>
    public string? SessionStateJson { get; init; }

    /// <summary>Gets detailed run diagnostics produced by Common.AI.</summary>
    public AgentRunResult? Diagnostics { get; init; }

    /// <summary>Gets delegation and compaction events observed during the turn.</summary>
    public IReadOnlyList<AgentExecutionEvent> Events { get; init; } = [];
}
