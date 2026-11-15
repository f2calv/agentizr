using System.ComponentModel.DataAnnotations;

namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Searches the agent's server-owned knowledge binding; no tenant, collection or model is caller-selectable.</summary>
public sealed record SearchKnowledgeRequest
{
    /// <summary>Gets the short semantic search text, not an instruction to the answering model.</summary>
    /// <example>Everyday shopping vocabulary</example>
    [Required, StringLength(2_000, MinimumLength = 1)]
    public required string Query { get; init; }

    /// <summary>Gets the maximum returned passages.</summary>
    /// <example>3</example>
    [Range(1, 8)]
    public int MaximumResults { get; init; } = 3;

    /// <summary>Gets an optional narrower document level; it can never broaden the operator's grant.</summary>
    /// <example>A1</example>
    [RegularExpression("^(A1|A2|B1|B2|C1|C2)$")]
    public string? MaximumCefrLevel { get; init; }
}
