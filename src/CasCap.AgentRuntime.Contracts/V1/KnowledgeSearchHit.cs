namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>A bounded exact cosine match; similarity is not a calibrated relevance probability.</summary>
public sealed record KnowledgeSearchHit
{
    /// <summary>Gets the authorized passage and its source reference.</summary>
    /// <example>{}</example>
    public required KnowledgePassageResponse Passage { get; init; }

    /// <summary>Gets cosine similarity, from minus one to one.</summary>
    /// <example>0.85</example>
    public required double Similarity { get; init; }
}
