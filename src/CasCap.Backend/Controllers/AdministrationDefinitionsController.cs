using CasCap.AgentRuntime.Contracts.V1;
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using CasCap.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes definition administration of the selected app to its administrators.</summary>
/// <remarks>Activation and deactivation require the version the administrator observed as active.</remarks>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[ValidateAdministrationAntiforgery]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AdministrationDefinitionsController(AdministrationApiService administrationApiSvc) : ControllerBase
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
        var outcome = await administrationApiSvc.Definitions.PublishAsync(agentName, request, cancellationToken);
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

    /// <inheritdoc cref="AdministrationApiService.GetHistoryPageAsync"/>
    [HttpGet(AdministrationRoutes.Definitions, Name = "GetAdministeredDefinitionHistory")]
    [EndpointSummary("Gets one newest-first page of definition versions with archive state and changed properties")]
    public async Task<Ok<AdministrationDefinitionHistoryPageResponse>> GetHistoryAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken,
        [FromQuery] DefinitionHistoryFilter filter = DefinitionHistoryFilter.Current,
        [FromQuery, Range(1, 10_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20) =>
        TypedResults.Ok(await administrationApiSvc.GetHistoryPageAsync(agentName, filter, page, pageSize, cancellationToken));

    /// <inheritdoc cref="AdministrationApiService.SetArchivedAsync"/>
    [HttpPost(AdministrationRoutes.DefinitionArchive, Name = "ArchiveAdministeredDefinition")]
    [EndpointSummary("Hides an inactive version from the default history without changing it")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> ArchiveAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        ToResult(await administrationApiSvc.SetArchivedAsync(agentName, definitionVersion, archived: true, cancellationToken));

    /// <inheritdoc cref="AdministrationApiService.SetArchivedAsync"/>
    [HttpPost(AdministrationRoutes.DefinitionRestore, Name = "RestoreAdministeredDefinition")]
    [EndpointSummary("Returns an archived version to the default history")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> RestoreAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        ToResult(await administrationApiSvc.SetArchivedAsync(agentName, definitionVersion, archived: false, cancellationToken));

    /// <inheritdoc cref="AdministrationApiService.DeleteAsync"/>
    [HttpDelete(AdministrationRoutes.DefinitionVersion, Name = "DeleteAdministeredDefinition")]
    [EndpointSummary("Permanently deletes a version that is not active and was never activated")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult>> DeleteAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        ToResult(await administrationApiSvc.DeleteAsync(agentName, definitionVersion, cancellationToken));

    /// <inheritdoc cref="AgentDefinitionApiService.GetActiveAsync"/>
    [HttpGet(AdministrationRoutes.ActiveDefinition, Name = "GetAdministeredActiveDefinition")]
    [EndpointSummary("Gets the active immutable definition")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetActiveAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        CancellationToken cancellationToken) =>
        await administrationApiSvc.Definitions.GetActiveAsync(agentName, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    /// <inheritdoc cref="AgentDefinitionApiService.GetVersionAsync"/>
    [HttpGet(AdministrationRoutes.DefinitionVersion, Name = "GetAdministeredDefinitionVersion")]
    [EndpointSummary("Gets one immutable definition version")]
    public async Task<Results<Ok<AgentDefinitionSnapshotResponse>, NotFound>> GetVersionAsync(
        [StringLength(200, MinimumLength = 1)] string agentName,
        [StringLength(100, MinimumLength = 1)] string definitionVersion,
        CancellationToken cancellationToken) =>
        await administrationApiSvc.Definitions.GetVersionAsync(agentName, definitionVersion, cancellationToken) is { } snapshot
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
        (await administrationApiSvc.Definitions.EvaluateAsync(agentName, definitionVersion, request, cancellationToken)) switch
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
        TypedResults.Ok(await administrationApiSvc.Definitions.GetActivationHistoryAsync(agentName, cancellationToken, limit));

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

    private static Results<NoContent, NotFound, ValidationProblem, StatusCodeHttpResult> ToResult(
        AgentDefinitionVersionChangeResult result) =>
        result switch
        {
            AgentDefinitionVersionChangeResult.Applied => TypedResults.NoContent(),
            AgentDefinitionVersionChangeResult.NotSupported => TypedResults.StatusCode(StatusCodes.Status501NotImplemented),
            AgentDefinitionVersionChangeResult.Active => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["definitionVersion"] = ["The active version cannot be archived or deleted. Activate another version first."],
            }),
            AgentDefinitionVersionChangeResult.PreviouslyActivated => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["definitionVersion"] = ["A version that was ever activated stays in the audit history. Archive it instead."],
            }),
            _ => TypedResults.NotFound(),
        };
}
