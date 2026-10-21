namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports the complete runtime override state for one agent session.</summary>
public sealed record AgentOverridesResponse
{
    /// <summary>Gets whether conversation state is loaded and persisted.</summary>
    /// <example>true</example>
    public bool SessionEnabled { get; init; } = true;

    /// <summary>Gets the model override, or <see langword="null" /> for the definition default.</summary>
    /// <example>qwen3:32b</example>
    [MaxLength(500)]
    public string? ModelName { get; init; }

    /// <summary>Gets the instruction override, or <see langword="null" /> for the definition default.</summary>
    /// <example>Answer concisely.</example>
    [MaxLength(100_000)]
    public string? Instructions { get; init; }
}
