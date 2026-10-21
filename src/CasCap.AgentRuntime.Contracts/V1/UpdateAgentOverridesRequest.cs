namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Replaces the complete runtime override state for one agent session.</summary>
public sealed record UpdateAgentOverridesRequest
{
    /// <summary>Gets whether conversation state should be loaded and persisted.</summary>
    /// <example>true</example>
    [JsonRequired]
    public bool SessionEnabled { get; init; }

    /// <summary>Gets the model override, or <see langword="null" /> to restore the definition default.</summary>
    /// <example>qwen3:32b</example>
    [MaxLength(500)]
    public string? ModelName { get; init; }

    /// <summary>Gets the instruction override, or <see langword="null" /> to restore the definition default.</summary>
    /// <example>Answer concisely.</example>
    [MaxLength(100_000)]
    public string? Instructions { get; init; }
}
