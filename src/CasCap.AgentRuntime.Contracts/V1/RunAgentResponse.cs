namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Returns the output and resolved definition version for one agent turn.</summary>
public sealed record RunAgentResponse
{
    /// <summary>Gets the caller-owned session identifier.</summary>
    /// <example>conversation-42</example>
    public required string SessionId { get; init; }

    /// <summary>Gets the agent output text.</summary>
    /// <example>All monitored services are healthy.</example>
    public required string OutputText { get; init; }

    /// <summary>Gets the version of the tenant agent definition used for this turn.</summary>
    /// <example>1</example>
    public required string DefinitionVersion { get; init; }
}
