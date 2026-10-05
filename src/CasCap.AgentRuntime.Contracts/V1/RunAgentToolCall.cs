namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Identifies one tool invoked during an agent turn.</summary>
public sealed record RunAgentToolCall
{
    /// <summary>Gets the tool name.</summary>
    /// <example>get_system_status</example>
    public required string Name { get; init; }
}
