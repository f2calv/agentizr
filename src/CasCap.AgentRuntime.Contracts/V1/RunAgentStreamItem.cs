namespace CasCap.AgentRuntime.Contracts.V1;

/// <summary>Represents one incremental event or the final response from a streamed agent run.</summary>
public sealed record RunAgentStreamItem
{
    /// <summary>Gets an incremental delegation or compaction event.</summary>
    public RunAgentEvent? Event { get; init; }

    /// <summary>Gets the final run response.</summary>
    public RunAgentResponse? Response { get; init; }
}
