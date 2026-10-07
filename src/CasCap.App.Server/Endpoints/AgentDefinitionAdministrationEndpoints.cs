using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using CasCap.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps version 1 definition-management endpoints.</summary>
public static class AgentDefinitionAdministrationEndpoints
{
    /// <summary>Maps the scoped definition control plane onto an agent route group.</summary>
    public static RouteGroupBuilder MapAgentDefinitionAdministration(
        this RouteGroupBuilder group,
        bool authorizationRequired)
    {
        var publish = group.MapPost(AgentRuntimeRoutes.Definitions, PublishAsync)
            .WithName("PublishAgentDefinitionV1")
            .WithSummary("Publishes an immutable agent definition without activating it")
            .Produces<AgentDefinitionSnapshotResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        var history = group.MapGet(AgentRuntimeRoutes.Definitions, GetHistoryAsync)
            .WithName("GetAgentDefinitionHistoryV1")
            .WithSummary("Gets newest-first immutable definition history")
            .Produces<IReadOnlyList<AgentDefinitionHistoryItemResponse>>();
        var active = group.MapGet(AgentRuntimeRoutes.ActiveDefinition, GetActiveAsync)
            .WithName("GetActiveAgentDefinitionV1")
            .WithSummary("Gets the active immutable definition")
            .Produces<AgentDefinitionSnapshotResponse>()
            .Produces(StatusCodes.Status404NotFound);
        var version = group.MapGet(AgentRuntimeRoutes.DefinitionVersion, GetVersionAsync)
            .WithName("GetAgentDefinitionVersionV1")
            .WithSummary("Gets one immutable definition version")
            .Produces<AgentDefinitionSnapshotResponse>()
            .Produces(StatusCodes.Status404NotFound);
        var activate = group.MapPut(AgentRuntimeRoutes.DefinitionActivation, ActivateAsync)
            .WithName("ActivateAgentDefinitionV1")
            .WithSummary("Activates or rolls back to an immutable definition version")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
        var activations = group.MapGet(AgentRuntimeRoutes.DefinitionActivations, GetActivationHistoryAsync)
            .WithName("GetAgentDefinitionActivationHistoryV1")
            .WithSummary("Gets newest-first activation and rollback history")
            .Produces<IReadOnlyList<AgentDefinitionActivationResponse>>();

        if (authorizationRequired)
        {
            publish.RequireAuthorization(AgentRuntimePolicies.DefinitionPublish);
            history.RequireAuthorization(AgentRuntimePolicies.DefinitionRead);
            active.RequireAuthorization(AgentRuntimePolicies.DefinitionRead);
            version.RequireAuthorization(AgentRuntimePolicies.DefinitionRead);
            activate.RequireAuthorization(AgentRuntimePolicies.DefinitionActivate);
            activations.RequireAuthorization(AgentRuntimePolicies.DefinitionRead);
        }
        return group;
    }

    private static async Task<Results<Created<AgentDefinitionSnapshotResponse>, ValidationProblem, Conflict, StatusCodeHttpResult>> PublishAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        PublishAgentDefinitionRequest request,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken)
    {
        var outcome = await administrationSvc.PublishAsync(agentName, request, cancellationToken);
        if (outcome.ValidationError is { } validationError)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Definition)] = [validationError],
            });
        }
        if (outcome.IsConflict)
            return TypedResults.Conflict();
        if (!outcome.IsSupported)
            return TypedResults.StatusCode(StatusCodes.Status501NotImplemented);

        var snapshot = MapSnapshot(outcome.Snapshot!);
        var location = $"/api/v1/agents/{Uri.EscapeDataString(agentName)}/definitions/"
            + Uri.EscapeDataString(snapshot.DefinitionVersion);
        return TypedResults.Created(location, snapshot);
    }

    private static async Task<IReadOnlyList<AgentDefinitionHistoryItemResponse>> GetHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken,
        [Range(1, 1_000)] int limit = 100) =>
        (await administrationSvc.GetHistoryAsync(agentName, limit, cancellationToken))
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

    private static async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetActiveAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken) =>
        await administrationSvc.GetActiveAsync(agentName, cancellationToken) is { } snapshot
            ? TypedResults.Ok(MapSnapshot(snapshot))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetVersionAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken) =>
        await administrationSvc.GetVersionAsync(agentName, definitionVersion, cancellationToken) is { } snapshot
            ? TypedResults.Ok(MapSnapshot(snapshot))
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> ActivateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        ActivateAgentDefinitionRequest request,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken) =>
        (await administrationSvc.ActivateAsync(
            agentName,
            definitionVersion,
            request.ChangeReason,
            cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(definitionVersion)] = [validationError] }),
            { Activated: true } => TypedResults.NoContent(),
            _ => TypedResults.NotFound(),
        };

    private static async Task<IReadOnlyList<AgentDefinitionActivationResponse>> GetActivationHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        AgentDefinitionAdministrationService administrationSvc,
        CancellationToken cancellationToken,
        [Range(1, 1_000)] int limit = 100) =>
        (await administrationSvc.GetActivationHistoryAsync(agentName, limit, cancellationToken))
            .Select(item => new AgentDefinitionActivationResponse
            {
                DefinitionVersion = item.DefinitionVersion,
                ActivatedAtUtc = item.ActivatedAtUtc,
                ActivatedBy = item.ActivatedBy,
                ChangeReason = item.ChangeReason,
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
