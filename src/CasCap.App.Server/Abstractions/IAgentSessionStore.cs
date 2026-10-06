namespace CasCap.Abstractions;

/// <summary>Persists serialized agent session state within the current tenant scope.</summary>
public interface IAgentSessionStore
{
    /// <summary>Gets serialized session state, or <see langword="null" /> when no session exists.</summary>
    ValueTask<string?> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Persists serialized session state.</summary>
    ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string sessionStateJson,
        CancellationToken cancellationToken);

    /// <summary>Deletes session state.</summary>
    ValueTask DeleteAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken);
}
