namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>One graded run of one evaluation scenario.</summary>
public sealed record AgentEvaluationRunResponse
{
    /// <summary>Gets the scenario identifier.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>Gets the one-based repetition number.</summary>
    public int Repetition { get; init; }

    /// <summary>Gets whether both the answer and the tool selection passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Gets whether the final answer satisfied the expectation.</summary>
    public bool AnswerPassed { get; init; }

    /// <summary>Gets whether required, forbidden and actuating tool rules passed.</summary>
    public bool ToolSelectionPassed { get; init; }

    /// <summary>Gets the final answer generated from the fixtures.</summary>
    public string Answer { get; init; } = string.Empty;

    /// <summary>Gets why the run failed; empty when it passed.</summary>
    public IReadOnlyList<string> FailureReasons { get; init; } = [];

    /// <summary>Gets model-facing tool names in the order they were called.</summary>
    public IReadOnlyList<string> ToolCalls { get; init; } = [];

    /// <summary>Gets the run's wall-clock duration in milliseconds.</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>Gets the exception type and message when the run did not complete.</summary>
    public string? Error { get; init; }
}
