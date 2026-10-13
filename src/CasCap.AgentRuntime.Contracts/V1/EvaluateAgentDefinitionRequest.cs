namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>
/// Evaluates an immutable agent definition against fixture-backed scenarios before activation.
/// No tool is executed: query and delegation tools return the scenario's fixtures, and actuating tools are
/// recorded but never performed.
/// </summary>
public sealed record EvaluateAgentDefinitionRequest
{
    /// <summary>Gets the scenarios put to the agent.</summary>
    [Required, MinLength(1), MaxLength(50)]
    public required IReadOnlyList<AgentEvaluationScenarioRequest> Scenarios { get; init; }

    /// <summary>Gets how many times each scenario runs, to expose non-deterministic model behaviour.</summary>
    /// <example>3</example>
    [Range(1, 10)]
    public int Repetitions { get; init; } = 1;

    /// <summary>Gets the fraction of runs that must pass for the evaluation to pass.</summary>
    /// <example>1</example>
    [Range(0d, 1d)]
    public double MinimumPassRate { get; init; } = 1d;
}
