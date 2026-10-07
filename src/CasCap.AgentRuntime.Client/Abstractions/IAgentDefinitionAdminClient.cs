namespace CasCap.AgentRuntime.Client.Abstractions;

/// <summary>Administers immutable tenant agent definitions through the control-plane API.</summary>
public interface IAgentDefinitionAdminClient
{
    /// <summary>Publishes an immutable snapshot without activating it.</summary>
    Task<AgentDefinitionSnapshotResponse> PublishAsync(
        string agentName,
        PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Gets the active snapshot, or <see langword="null" /> when none is active.</summary>
    Task<AgentDefinitionSnapshotResponse?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken);

    /// <summary>Gets one immutable version, or <see langword="null" /> when it does not exist.</summary>
    Task<AgentDefinitionSnapshotResponse?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first immutable snapshot history.</summary>
    Task<IReadOnlyList<AgentDefinitionHistoryItemResponse>> GetHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Activates an existing version.</summary>
    Task<bool> ActivateAsync(
        string agentName,
        string definitionVersion,
        ActivateAgentDefinitionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    Task<IReadOnlyList<AgentDefinitionActivationResponse>> GetActivationHistoryAsync(
        string agentName,
        int limit,
        CancellationToken cancellationToken);
}
