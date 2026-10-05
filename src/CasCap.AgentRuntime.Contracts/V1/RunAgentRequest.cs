namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Requests one turn against a named agent and caller-owned session.</summary>
public sealed record RunAgentRequest
{
    /// <summary>Gets the caller-owned session identifier.</summary>
    /// <example>conversation-42</example>
    [Required, MinLength(1), MaxLength(200)]
    public required string SessionId { get; init; }

    /// <summary>Gets the user input for this turn.</summary>
    /// <example>Summarize the latest system status.</example>
    [Required, MinLength(1), MaxLength(100_000)]
    public required string Input { get; init; }
}
