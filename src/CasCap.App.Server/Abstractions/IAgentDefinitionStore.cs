namespace CasCap.Abstractions;

/// <summary>Resolves versioned agent definitions for the current tenant.</summary>
public interface IAgentDefinitionStore
{
    /// <summary>Gets the named definition, or <see langword="null" /> when it is not available to the tenant.</summary>
    ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken);

    /// <summary>Publishes an immutable definition snapshot and makes it active atomically.</summary>
    ValueTask PublishAsync(
        AgentDefinition definition,
        int schemaVersion,
        string? publishedBy,
        string? changeReason,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first immutable definition history for the named tenant agent.</summary>
    ValueTask<IReadOnlyList<AgentDefinitionHistoryItem>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);
}
