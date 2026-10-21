namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Outcome of evaluating one immutable definition version.</summary>
public sealed record AgentEvaluationResponse
{
    /// <summary>Gets the evaluated agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the evaluated definition version.</summary>
    public required string DefinitionVersion { get; init; }

    /// <summary>Gets whether the pass rate met the requested minimum.</summary>
    public bool Passed { get; init; }

    /// <summary>Gets the fraction of runs that passed.</summary>
    public double PassRate { get; init; }

    /// <summary>Gets the requested minimum pass rate.</summary>
    public double MinimumPassRate { get; init; }

    /// <summary>Gets every graded run in scenario and repetition order.</summary>
    public IReadOnlyList<AgentEvaluationRunResponse> Runs { get; init; } = [];
}
