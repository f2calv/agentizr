namespace CasCap.Models;

/// <summary>Output from one agent execution and its optional updated session state.</summary>
public sealed record AgentExecutionResult
{
    /// <summary>Gets the agent's output text.</summary>
    public required string OutputText { get; init; }

    /// <summary>Gets the version of the resolved tenant definition.</summary>
    public string DefinitionVersion { get; init; } = string.Empty;

    /// <summary>Gets serialized updated session state, or <see langword="null" /> when no state should be persisted.</summary>
    public string? SessionStateJson { get; init; }
}
