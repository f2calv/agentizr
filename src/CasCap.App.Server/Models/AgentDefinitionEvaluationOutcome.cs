using CasCap.AgentRuntime.Contracts.V1;

namespace CasCap.Models;

/// <summary>Result of evaluating an immutable definition version.</summary>
public sealed record AgentDefinitionEvaluationOutcome
{
    /// <summary>Gets the graded evaluation, or <see langword="null" /> when the version does not exist or the request is invalid.</summary>
    public AgentEvaluationResponse? Evaluation { get; init; }

    /// <summary>Gets a public-safe validation error.</summary>
    public string? ValidationError { get; init; }
}
