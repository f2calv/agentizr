namespace CasCap.Abstractions;

/// <summary>Publishes, reads and activates immutable definitions for the current tenant.</summary>
public interface IAgentDefinitionAdministrationStore
{
    /// <summary>Publishes an immutable definition snapshot without activating it.</summary>
    public ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken);

    /// <summary>Gets the active immutable snapshot, or <see langword="null" /> when none is active.</summary>
    public ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken);

    /// <summary>Gets one immutable snapshot by version.</summary>
    public ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first immutable definition history.</summary>
    public ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Activates an existing immutable version and appends an audit event.</summary>
    public ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Serializes activations across every agent of the current tenant until the returned handle is
    /// disposed, so a delegation graph validated under the lock cannot be invalidated concurrently.
    /// </summary>
    public ValueTask<IAsyncDisposable> AcquireTenantActivationLockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the agent's active pointer and appends a deactivation audit event, or returns
    /// <see langword="false" /> when no version is active.
    /// </summary>
    public ValueTask<bool> DeactivateAsync(
        string agentName,
        string? deactivatedBy,
        string changeReason,
        CancellationToken cancellationToken);

    /// <summary>Gets every active definition of the current tenant.</summary>
    public ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);
}
