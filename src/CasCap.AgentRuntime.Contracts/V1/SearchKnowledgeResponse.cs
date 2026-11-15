namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Returns current authorized passages, not a generated answer or proof of strict vocabulary enforcement.</summary>
public sealed record SearchKnowledgeResponse
{
    /// <summary>Gets descending-similarity matches, with stable generation/ordinal tie-breaks.</summary>
    /// <example>[]</example>
    public required IReadOnlyList<KnowledgeSearchHit> Matches { get; init; }

    /// <summary>Gets native provider-reported input usage; zero when no eligible corpus required inference.</summary>
    /// <example>8</example>
    public required long EmbeddingInputTokens { get; init; }
}
