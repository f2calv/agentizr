namespace CasCap.AgentRuntime.Client.Abstractions;

/// <summary>Runs versioned agent turns through the remote Agent Runtime.</summary>
public interface IAgentRuntimeClient
{
    /// <summary>Runs one agent turn, or returns <see langword="null" /> when the agent is unavailable to the tenant.</summary>
    Task<RunAgentResponse?> RunAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken);
}
