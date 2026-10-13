namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>One question, the fixture state its tools return, and how the run is graded.</summary>
public sealed record AgentEvaluationScenarioRequest
{
    /// <summary>Gets the stable scenario identifier used in results.</summary>
    /// <example>current-temperature</example>
    [Required, MinLength(1), MaxLength(100)]
    public required string Id { get; init; }

    /// <summary>Gets the user message, phrased the way a real user would send it.</summary>
    /// <example>How warm is the living room?</example>
    [Required, MinLength(1), MaxLength(10_000)]
    public required string Question { get; init; }

    /// <summary>Gets the check applied to the final answer.</summary>
    public AgentEvaluationAnswerRequest Answer { get; init; } = new();

    /// <summary>
    /// Gets tool groups the run must call. Each group is satisfied when any one of its tool names was called.
    /// </summary>
    [MaxLength(50)]
    public IReadOnlyList<string[]> RequiredToolGroups { get; init; } = [];

    /// <summary>Gets tool names the run must not call.</summary>
    [MaxLength(100)]
    public IReadOnlyList<string> ForbiddenTools { get; init; } = [];

    /// <summary>Gets whether attempting any actuating tool fails the run.</summary>
    public bool SideEffectsForbidden { get; init; } = true;

    /// <summary>
    /// Gets fixture responses keyed by model-facing tool name. A query tool without a fixture reports that no
    /// data is available; delegation tools return their fixture in place of the sub-agent's answer.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> ToolResponses { get; init; } = new Dictionary<string, JsonElement>();
}
