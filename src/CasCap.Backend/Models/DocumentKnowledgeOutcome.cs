using CasCap.AgentRuntime.Contracts.V1;

namespace CasCap.Models;

/// <summary>Distinguishes unavailable bindings, invalid inputs, upstream failures and successful retrieval.</summary>
public sealed record DocumentKnowledgeOutcome
{
    /// <summary>Gets successful matches, including a legitimate empty eligible corpus.</summary>
    public SearchKnowledgeResponse? Response { get; init; }

    /// <summary>Gets a safe caller-actionable validation failure.</summary>
    public string? ValidationError { get; init; }

    /// <summary>Gets whether provider verification or inference failed; this is never an empty success.</summary>
    public bool ProviderFailed { get; init; }

    /// <summary>Gets whether the cooperative search deadline expired.</summary>
    public bool TimedOut { get; init; }
}
