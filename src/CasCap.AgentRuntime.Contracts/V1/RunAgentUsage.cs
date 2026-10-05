namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Reports provider token usage for one complete agent turn.</summary>
public sealed record RunAgentUsage
{
    /// <summary>Gets the input token count when reported by the provider.</summary>
    /// <example>240</example>
    public long? InputTokenCount { get; init; }

    /// <summary>Gets the output token count when reported by the provider.</summary>
    /// <example>80</example>
    public long? OutputTokenCount { get; init; }

    /// <summary>Gets the total token count when reported by the provider.</summary>
    /// <example>320</example>
    public long? TotalTokenCount { get; init; }
}
