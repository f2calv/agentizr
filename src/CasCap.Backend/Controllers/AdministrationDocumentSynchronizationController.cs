#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using CasCap.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Allows signed-in app administrators to request owner-executed source synchronization.</summary>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[Authorize(Policy = AdministrationPolicies.AuthenticatedDocumentReviewer)]
[ValidateAdministrationAntiforgery]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(StatusCodes.Status501NotImplemented)]
public sealed class AdministrationDocumentSynchronizationController(DocumentSynchronizationAdministrationService synchronizationSvc) : ControllerBase
{
    /// <inheritdoc cref="DocumentSynchronizationAdministrationService.ReadAsync"/>
    [HttpGet(DocumentCatalogueRoutes.Synchronization, Name = "GetDocumentSynchronization")]
    public async Task<Results<Ok<DocumentSynchronizationResponse>, StatusCodeHttpResult>> ReadAsync(CancellationToken cancellationToken) =>
        await synchronizationSvc.ReadAsync(cancellationToken) is { } result
            ? TypedResults.Ok(result)
            : TypedResults.StatusCode(StatusCodes.Status501NotImplemented);

    /// <inheritdoc cref="DocumentSynchronizationAdministrationService.RequestAsync"/>
    [HttpPost(DocumentCatalogueRoutes.Synchronization, Name = "RequestDocumentSynchronization")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<Results<Accepted<DocumentSynchronizationResponse>, StatusCodeHttpResult>> RequestAsync(
        string appId, CancellationToken cancellationToken) =>
        await synchronizationSvc.RequestAsync(cancellationToken) is { } result
            ? TypedResults.Accepted(AdministrationRoutes.Format(DocumentCatalogueRoutes.Synchronization, appId), result)
            : TypedResults.StatusCode(StatusCodes.Status501NotImplemented);
}
#endif
