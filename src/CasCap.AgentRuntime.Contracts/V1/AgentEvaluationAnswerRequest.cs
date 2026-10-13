namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>
/// Checks applied to a final answer. Every populated check must pass; with none populated, any non-empty
/// answer passes.
/// </summary>
public sealed record AgentEvaluationAnswerRequest
{
    /// <summary>Gets phrases of which at least one must appear, compared case-insensitively.</summary>
    /// <example>["21", "twenty-one"]</example>
    [MaxLength(50)]
    public IReadOnlyList<string> ContainsAny { get; init; } = [];

    /// <summary>Gets phrases that must not appear as whole words, compared case-insensitively.</summary>
    [MaxLength(50)]
    public IReadOnlyList<string> ContainsNone { get; init; } = [];

    /// <summary>Gets a case-insensitive regular expression the answer must match.</summary>
    /// <example>\b21(\.\d+)?\s*°?C\b</example>
    [MaxLength(1_000)]
    public string? Pattern { get; init; }

    /// <summary>Gets a number the answer must state, as digits or English words up to twenty.</summary>
    /// <example>21.5</example>
    public double? Number { get; init; }

    /// <summary>Gets the absolute tolerance applied to <see cref="Number" />.</summary>
    /// <example>0.5</example>
    [Range(0d, double.MaxValue)]
    public double NumberTolerance { get; init; }
}
