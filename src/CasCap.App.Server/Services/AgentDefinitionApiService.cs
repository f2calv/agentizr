using CasCap.AgentRuntime.Contracts.V1;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Maps definition administration outcomes and snapshots to version 1 DTOs.</summary>
public sealed class AgentDefinitionApiService
{
    private readonly AgentDefinitionAdministrationService _administrationSvc;

    /// <summary>Creates the DTO service without exposing internal administration dependencies.</summary>
    internal AgentDefinitionApiService(AgentDefinitionAdministrationService administrationSvc) =>
        _administrationSvc = administrationSvc;

    /// <summary>Publishes an immutable agent definition without activating it.</summary>
    public async Task<AgentDefinitionPublicationResult> PublishAsync(
        string agentName,
        PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await _administrationSvc.PublishAsync(agentName, request, cancellationToken);
        return new AgentDefinitionPublicationResult
        {
            Snapshot = outcome.Snapshot is { } snapshot ? MapSnapshot(snapshot) : null,
            ValidationError = outcome.ValidationError,
            IsConflict = outcome.IsConflict,
            IsSupported = outcome.IsSupported,
        };
    }

    /// <summary>Gets newest-first immutable definition history.</summary>
    public async Task<IReadOnlyList<AgentDefinitionHistoryItemResponse>> GetHistoryAsync(
        string agentName,
        CancellationToken cancellationToken,
        int limit = 100) =>
        (await _administrationSvc.GetHistoryAsync(agentName, limit, cancellationToken))
            .Select(item => new AgentDefinitionHistoryItemResponse
            {
                DefinitionVersion = item.DefinitionVersion,
                SchemaVersion = item.SchemaVersion,
                PublishedAtUtc = item.PublishedAtUtc,
                PublishedBy = item.PublishedBy,
                ChangeReason = item.ChangeReason,
                IsActive = item.IsActive,
            })
            .ToArray();

    /// <summary>Gets the active immutable definition.</summary>
    public async Task<AgentDefinitionSnapshotResponse?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken) =>
        await _administrationSvc.GetActiveAsync(agentName, cancellationToken) is { } snapshot
            ? MapSnapshot(snapshot)
            : null;

    /// <summary>Gets one immutable definition version.</summary>
    public async Task<AgentDefinitionSnapshotResponse?> GetVersionAsync(
        string agentName,
        string definitionVersion,
        CancellationToken cancellationToken) =>
        await _administrationSvc.GetVersionAsync(agentName, definitionVersion, cancellationToken) is { } snapshot
            ? MapSnapshot(snapshot)
            : null;

    /// <summary>Evaluates an immutable definition version against fixture scenarios without activating it.</summary>
    public ValueTask<AgentDefinitionEvaluationOutcome> EvaluateAsync(
        string agentName,
        string definitionVersion,
        EvaluateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        _administrationSvc.EvaluateAsync(agentName, definitionVersion, request, cancellationToken);

    /// <summary>Activates or rolls back to an immutable definition version, optionally gated by an evaluation.</summary>
    public ValueTask<AgentDefinitionActivationOutcome> ActivateAsync(
        string agentName,
        string definitionVersion,
        ActivateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        _administrationSvc.ActivateAsync(
            agentName,
            definitionVersion,
            request.ChangeReason,
            request.Evaluation,
            cancellationToken);

    /// <summary>Deactivates the active definition, leaving the agent with no active version.</summary>
    public ValueTask<AgentDefinitionDeactivationOutcome> DeactivateAsync(
        string agentName,
        DeactivateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        _administrationSvc.DeactivateAsync(agentName, request.ChangeReason, cancellationToken);

    /// <summary>Gets newest-first activation and rollback history.</summary>
    public async Task<IReadOnlyList<AgentDefinitionActivationResponse>> GetActivationHistoryAsync(
        string agentName,
        CancellationToken cancellationToken,
        int limit = 100) =>
        (await _administrationSvc.GetActivationHistoryAsync(agentName, limit, cancellationToken))
            .Select(item => new AgentDefinitionActivationResponse
            {
                DefinitionVersion = item.DefinitionVersion,
                ActivatedAtUtc = item.ActivatedAtUtc,
                ActivatedBy = item.ActivatedBy,
                ChangeReason = item.ChangeReason,
                IsDeactivation = item.IsDeactivation,
            })
            .ToArray();

    private static AgentDefinitionSnapshotResponse MapSnapshot(AgentDefinitionSnapshotItem snapshot) => new()
    {
        AgentName = snapshot.Definition.Name,
        DefinitionVersion = snapshot.Definition.Version,
        SchemaVersion = snapshot.SchemaVersion,
        Definition = JsonSerializer.SerializeToElement(snapshot.Definition, JsonSerializerOptions.Web),
        PublishedAtUtc = snapshot.PublishedAtUtc,
        PublishedBy = snapshot.PublishedBy,
        ChangeReason = snapshot.ChangeReason,
        IsActive = snapshot.IsActive,
    };
}
