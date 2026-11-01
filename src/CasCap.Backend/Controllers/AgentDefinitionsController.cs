using CasCap.AgentRuntime.Contracts.V1;
using CasCap.AgentRuntime.Contracts.V1.Constants;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes version 1 definition administration with separate read, publish and activation policies.</summary>
[ApiController]
[Tags("Agent Definitions")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AgentDefinitionsController(AgentDefinitionApiService definitionApiSvc) : ControllerBase
{
    /// <inheritdoc cref="AgentDefinitionApiService.PublishAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.Definitions, Name = "PublishAgentDefinitionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionPublish)]
    [EndpointSummary("Publishes an immutable agent definition without activating it")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<Created<AgentDefinitionSnapshotResponse>, ValidationProblem, Conflict, StatusCodeHttpResult>> PublishAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [FromBody] PublishAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await definitionApiSvc.PublishAsync(agentName, request, cancellationToken);
        if (outcome.ValidationError is { } validationError)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Definition)] = [validationError],
            });
        if (outcome.IsConflict)
            return TypedResults.Conflict();
        if (!outcome.IsSupported)
            return TypedResults.StatusCode(StatusCodes.Status501NotImplemented);

        var snapshot = outcome.Snapshot!;
        var location = AgentRuntimeRoutes.AgentGroup.Replace("{agentName}", Uri.EscapeDataString(agentName), StringComparison.Ordinal)
            + AgentRuntimeRoutes.DefinitionVersion.Replace("{definitionVersion}", Uri.EscapeDataString(snapshot.DefinitionVersion), StringComparison.Ordinal);
        return TypedResults.Created(location, snapshot);
    }

    /// <inheritdoc cref="AgentDefinitionApiService.GetHistoryAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.Definitions, Name = "GetAgentDefinitionHistoryV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionRead)]
    [EndpointSummary("Gets newest-first immutable definition history")]
    public async Task<Ok<IReadOnlyList<AgentDefinitionHistoryItemResponse>>> GetHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 1_000)] int limit = 100) =>
        TypedResults.Ok(await definitionApiSvc.GetHistoryAsync(agentName, cancellationToken, limit));

    /// <inheritdoc cref="AgentDefinitionApiService.GetActiveAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.ActiveDefinition, Name = "GetActiveAgentDefinitionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionRead)]
    [EndpointSummary("Gets the active immutable definition")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetActiveAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken) =>
        await definitionApiSvc.GetActiveAsync(agentName, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentDefinitionApiService.GetVersionAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.DefinitionVersion, Name = "GetAgentDefinitionVersionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionRead)]
    [EndpointSummary("Gets one immutable definition version")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetVersionAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        await definitionApiSvc.GetVersionAsync(agentName, definitionVersion, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentDefinitionApiService.EvaluateAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.DefinitionEvaluation, Name = "EvaluateAgentDefinitionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionActivate)]
    [EndpointSummary("Evaluates an immutable definition version against fixture scenarios without activating it")]
    public async Task<Results<Ok<AgentEvaluationResponse>, NotFound, ValidationProblem>> EvaluateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        [FromBody] EvaluateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        (await definitionApiSvc.EvaluateAsync(agentName, definitionVersion, request, cancellationToken)) switch
        {
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(request)] = [validationError] }),
            { Evaluation: { } evaluation } => TypedResults.Ok(evaluation),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="AgentDefinitionApiService.ActivateAsync"/>
    [HttpPut(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.DefinitionActivation, Name = "ActivateAgentDefinitionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionActivate)]
    [EndpointSummary("Activates or rolls back to an immutable definition version, optionally gated by an evaluation")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> ActivateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        [FromBody] ActivateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        (await definitionApiSvc.ActivateAsync(agentName, definitionVersion, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(definitionVersion)] = [validationError] }),
            { Activated: true } => TypedResults.NoContent(),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="AgentDefinitionApiService.GetActivationHistoryAsync"/>
    [HttpGet(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.DefinitionActivations, Name = "GetAgentDefinitionActivationHistoryV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionRead)]
    [EndpointSummary("Gets newest-first activation and rollback history")]
    public async Task<Ok<IReadOnlyList<AgentDefinitionActivationResponse>>> GetActivationHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 1_000)] int limit = 100) =>
        TypedResults.Ok(await definitionApiSvc.GetActivationHistoryAsync(agentName, cancellationToken, limit));

    /// <inheritdoc cref="AgentDefinitionApiService.DeactivateAsync"/>
    [HttpPost(AgentRuntimeRoutes.AgentGroup + AgentRuntimeRoutes.DefinitionDeactivation, Name = "DeactivateAgentDefinitionV1")]
    [Authorize(Policy = AgentRuntimePolicies.DefinitionActivate)]
    [EndpointSummary("Deactivates the active definition, leaving the agent with no active version")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> DeactivateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [FromBody] DeactivateAgentDefinitionRequest request,
        CancellationToken cancellationToken) =>
        (await definitionApiSvc.DeactivateAsync(agentName, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(agentName)] = [validationError] }),
            { Deactivated: true } => TypedResults.NoContent(),
            _ => TypedResults.NotFound(),
        };
}
