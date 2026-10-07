namespace CasCap.AgentRuntime.Client.Abstractions;

/// <summary>Runs versioned agent turns through the remote Agent Runtime.</summary>
public interface IAgentRuntimeClient
{
    /// <summary>Runs one agent turn, or returns <see langword="null" /> when the agent is unavailable to the tenant.</summary>
    public Task<RunAgentResponse?> RunAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Streams live execution events followed by the final response.</summary>
    public IAsyncEnumerable<RunAgentStreamItem> StreamAgentAsync(
        string agentName,
        RunAgentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Gets active session status, or <see langword="null" /> when the agent is unavailable.</summary>
    public Task<AgentSessionInfoResponse?> GetSessionAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Resets active session state.</summary>
    public Task<bool> ResetSessionAsync(string agentName, string sessionId, CancellationToken cancellationToken);

    /// <summary>Compacts active session history, or returns <see langword="null" /> when the agent is unavailable.</summary>
    public Task<CompactAgentSessionResponse?> CompactSessionAsync(
        string agentName,
        string sessionId,
        CompactAgentSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Saves active session state as a named snapshot.</summary>
    public Task<bool> SaveSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken);

    /// <summary>Loads a named snapshot into the active session.</summary>
    public Task<bool> LoadSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken);

    /// <summary>Deletes a named session snapshot.</summary>
    public Task<bool> DeleteSessionSnapshotAsync(
        string agentName,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken);

    /// <summary>Gets complete runtime overrides, or <see langword="null" /> when the agent is unavailable.</summary>
    public Task<AgentOverridesResponse?> GetOverridesAsync(
        string agentName,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Replaces complete runtime overrides, or returns <see langword="null" /> when the agent is unavailable.</summary>
    public Task<AgentOverridesResponse?> SetOverridesAsync(
        string agentName,
        string sessionId,
        UpdateAgentOverridesRequest request,
        CancellationToken cancellationToken);
}
