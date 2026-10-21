namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Requests deterministic compaction of an active agent session.</summary>
public sealed record CompactAgentSessionRequest
{
    /// <summary>Gets the number of recent chat messages to retain.</summary>
    /// <example>20</example>
    [JsonRequired, Range(1, 10_000)]
    public int RetainMessageCount { get; init; }
}
