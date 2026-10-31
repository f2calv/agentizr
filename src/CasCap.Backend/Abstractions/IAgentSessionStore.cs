namespace CasCap.Abstractions;

/// <summary>Persists serialized agent session state within the current tenant scope.</summary>
public interface IAgentSessionStore
{
    /// <summary>Gets serialized session state, or <see langword="null" /> when no session exists.</summary>
    public ValueTask<string?> GetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Persists serialized session state.</summary>
    public ValueTask SetAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string sessionStateJson,
        CancellationToken cancellationToken);

    /// <summary>Deletes session state.</summary>
    public ValueTask DeleteAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        CancellationToken cancellationToken);

    /// <summary>Gets a named snapshot of serialized session state.</summary>
    public ValueTask<string?> GetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken);

    /// <summary>Persists a named snapshot of serialized session state.</summary>
    public ValueTask SetSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        string sessionStateJson,
        CancellationToken cancellationToken);

    /// <summary>Deletes a named snapshot of serialized session state.</summary>
    public ValueTask DeleteSnapshotAsync(
        string agentName,
        string definitionVersion,
        string sessionId,
        string snapshotName,
        CancellationToken cancellationToken);
}
