namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Token usage the provider reported for a realtime voice call, summed over its responses.</summary>
public sealed record RealtimeCallUsage
{
    /// <summary>Gets the total input and output tokens.</summary>
    /// <example>2300</example>
    public long TotalTokens { get; init; }

    /// <summary>Gets the input text tokens, including instructions and tool results.</summary>
    public long InputTextTokens { get; init; }

    /// <summary>Gets the input audio tokens.</summary>
    public long InputAudioTokens { get; init; }

    /// <summary>Gets the input tokens served from the provider's prompt cache, of either modality.</summary>
    public long CachedInputTokens { get; init; }

    /// <summary>Gets the output text tokens, including transcripts and tool calls.</summary>
    public long OutputTextTokens { get; init; }

    /// <summary>Gets the output audio tokens.</summary>
    public long OutputAudioTokens { get; init; }
}
