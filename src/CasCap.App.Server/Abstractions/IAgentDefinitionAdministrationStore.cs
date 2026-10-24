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
    /// <remarks>
    /// When <paramref name="expectedActive"/> is supplied, the active version is compared atomically with the
    /// change and a mismatch throws <see cref="Exceptions.AgentDefinitionActiveVersionConflictException"/>.
    /// </remarks>
    public ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken,
        ExpectedActiveDefinition? expectedActive = null);

    /// <summary>
    /// Serializes activations across every agent of the current tenant until the returned handle is
    /// disposed, so a delegation graph validated under the lock cannot be invalidated concurrently.
    /// </summary>
    public ValueTask<IAsyncDisposable> AcquireTenantActivationLockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the agent's active pointer and appends a deactivation audit event, or returns
    /// <see langword="false" /> when no version is active.
    /// </summary>
    /// <remarks>
    /// When <paramref name="expectedActive"/> is supplied, the active version is compared atomically with the
    /// change and a mismatch throws <see cref="Exceptions.AgentDefinitionActiveVersionConflictException"/>.
    /// </remarks>
    public ValueTask<bool> DeactivateAsync(
        string agentName,
        string? deactivatedBy,
        string changeReason,
        CancellationToken cancellationToken,
        ExpectedActiveDefinition? expectedActive = null);

    /// <summary>Gets every active definition of the current tenant.</summary>
    public ValueTask<IReadOnlyList<AgentDefinition>> GetActiveDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>Gets one page of the current tenant's agents, active or not, ordered by name.</summary>
    /// <param name="nameFilter">An optional case-insensitive substring of the agent name.</param>
    /// <param name="skip">How many matching agents to skip.</param>
    /// <param name="take">The maximum number of agents to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public ValueTask<AgentDefinitionSummaryPage> GetAgentsAsync(
        string? nameFilter,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>Counts the agents with at least one published version in each requested tenant.</summary>
    /// <remarks>
    /// The only cross-tenant read: callers must have authorized every requested tenant. Tenants without
    /// agents are omitted.
    /// </remarks>
    public ValueTask<IReadOnlyDictionary<string, int>> CountAgentsAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    public ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);
}
