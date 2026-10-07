namespace CasCap.Abstractions;

/// <summary>Publishes, reads and activates immutable definitions for the current tenant.</summary>
public interface IAgentDefinitionAdministrationStore
{
    /// <summary>Publishes an immutable definition snapshot without activating it.</summary>
    ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken);

    /// <summary>Gets the active immutable snapshot, or <see langword="null" /> when none is active.</summary>
    ValueTask<AgentDefinitionSnapshotItem?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken);

    /// <summary>Gets one immutable snapshot by version.</summary>
    ValueTask<AgentDefinitionSnapshotItem?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first immutable definition history.</summary>
    ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Activates an existing immutable version and appends an audit event.</summary>
    ValueTask<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        string? activatedBy,
        string? changeReason,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    ValueTask<IReadOnlyList<AgentDefinitionActivationItem>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);
}
