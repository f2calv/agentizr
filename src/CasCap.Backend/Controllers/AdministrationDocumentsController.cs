#if DEBUG
using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes private document catalogue metadata only to signed-in administrators of the selected app.</summary>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[Authorize(Policy = AdministrationPolicies.AuthenticatedDocumentReviewer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status501NotImplemented)]
public sealed class AdministrationDocumentsController(DocumentCatalogueAdministrationService catalogueSvc) : ControllerBase
{
    /// <inheritdoc cref="DocumentCatalogueAdministrationService.ReadAsync"/>
    [HttpGet(DocumentCatalogueRoutes.Documents, Name = "GetAdministeredDocuments")]
    public async Task<Results<Ok<DocumentCataloguePageResponse>, StatusCodeHttpResult>> ReadAsync(
        CancellationToken cancellationToken,
        [FromQuery, StringLength(200, MinimumLength = 1)] string? collection = null,
        [FromQuery, Range(1, 10_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 25) =>
        await catalogueSvc.ReadAsync(collection, page, pageSize, cancellationToken) is { } result
            ? TypedResults.Ok(result)
            : TypedResults.StatusCode(StatusCodes.Status501NotImplemented);
}
#endif
