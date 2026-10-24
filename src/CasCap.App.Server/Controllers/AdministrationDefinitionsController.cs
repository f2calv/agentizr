using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes definition administration of the selected app to its administrators.</summary>
/// <remarks>Activation and deactivation require the version the administrator observed as active.</remarks>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AdministrationDefinitionsController(
    AgentDefinitionApiService definitionApiSvc,
    AdministrationApiService administrationApiSvc) : ControllerBase
{
    /// <inheritdoc cref="AgentDefinitionApiService.PublishAsync"/>
    [HttpPost(AdministrationRoutes.Definitions, Name = "PublishAdministeredDefinition")]
    [EndpointSummary("Publishes an immutable agent definition without activating it")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<Created<AgentDefinitionSnapshotResponse>, ValidationProblem, Conflict, StatusCodeHttpResult>> PublishAsync(
        string appId,
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
        return TypedResults.Created(
            AdministrationRoutes.Format(AdministrationRoutes.DefinitionVersion, appId, agentName, snapshot.DefinitionVersion),
            snapshot);
    }

    /// <inheritdoc cref="AgentDefinitionApiService.GetHistoryAsync"/>
    [HttpGet(AdministrationRoutes.Definitions, Name = "GetAdministeredDefinitionHistory")]
    [EndpointSummary("Gets newest-first immutable definition history")]
    public async Task<Ok<IReadOnlyList<AgentDefinitionHistoryItemResponse>>> GetHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 1_000)] int limit = 100) =>
        TypedResults.Ok(await definitionApiSvc.GetHistoryAsync(agentName, cancellationToken, limit));

    /// <inheritdoc cref="AgentDefinitionApiService.GetActiveAsync"/>
    [HttpGet(AdministrationRoutes.ActiveDefinition, Name = "GetAdministeredActiveDefinition")]
    [EndpointSummary("Gets the active immutable definition")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetActiveAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken) =>
        await definitionApiSvc.GetActiveAsync(agentName, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentDefinitionApiService.GetVersionAsync"/>
    [HttpGet(AdministrationRoutes.DefinitionVersion, Name = "GetAdministeredDefinitionVersion")]
    [EndpointSummary("Gets one immutable definition version")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetVersionAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        await definitionApiSvc.GetVersionAsync(agentName, definitionVersion, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentDefinitionApiService.EvaluateAsync"/>
    [HttpPost(AdministrationRoutes.DefinitionEvaluation, Name = "EvaluateAdministeredDefinition")]
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

    /// <inheritdoc cref="AdministrationApiService.ActivateAsync"/>
    [HttpPut(AdministrationRoutes.DefinitionActivation, Name = "ActivateAdministeredDefinition")]
    [EndpointSummary("Activates or rolls back to an immutable version if the observed active version is still current")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, Conflict, StatusCodeHttpResult>> ActivateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        [FromBody] AdministrationActivationRequest request,
        CancellationToken cancellationToken) =>
        (await administrationApiSvc.ActivateAsync(agentName, definitionVersion, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(definitionVersion)] = [validationError] }),
            { Activated: true } => TypedResults.NoContent(),
            _ => TypedResults.NotFound(),
        };

    /// <inheritdoc cref="AgentDefinitionApiService.GetActivationHistoryAsync"/>
    [HttpGet(AdministrationRoutes.DefinitionActivations, Name = "GetAdministeredActivationHistory")]
    [EndpointSummary("Gets newest-first activation and rollback history")]
    public async Task<Ok<IReadOnlyList<AgentDefinitionActivationResponse>>> GetActivationHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 1_000)] int limit = 100) =>
        TypedResults.Ok(await definitionApiSvc.GetActivationHistoryAsync(agentName, cancellationToken, limit));

    /// <inheritdoc cref="AdministrationApiService.DeactivateAsync"/>
    [HttpPost(AdministrationRoutes.DefinitionDeactivation, Name = "DeactivateAdministeredDefinition")]
    [EndpointSummary("Deactivates the active definition if it is still the observed version")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, Conflict, StatusCodeHttpResult>> DeactivateAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [FromBody] AdministrationDeactivationRequest request,
        CancellationToken cancellationToken) =>
        (await administrationApiSvc.DeactivateAsync(agentName, request, cancellationToken)) switch
        {
            { IsSupported: false } => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            { IsConflict: true } => TypedResults.Conflict(),
            { ValidationError: { } validationError } => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [nameof(agentName)] = [validationError] }),
            { Deactivated: true } => TypedResults.NoContent(),
            _ => TypedResults.NotFound(),
        };
}
