namespace CasCap.Abstractions;

/// <summary>Stores per-session runtime overrides within the current tenant scope.</summary>
public interface IAgentOverrideStore
{
    /// <summary>Gets the current override state.</summary>
    ValueTask<AgentOverrideState> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Persists the override state.</summary>
    ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        AgentOverrideState state,
        CancellationToken cancellationToken);
}
