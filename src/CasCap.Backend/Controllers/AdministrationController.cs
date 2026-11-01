using CasCap.Client.Constants;
using CasCap.Client.Models;
using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Exposes app, agent and provider discovery to administrators of the selected app.</summary>
[ApiController]
[Tags("Administration")]
[Authorize(Policy = AdministrationPolicies.AppAdministrator)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AdministrationController(AdministrationApiService administrationApiSvc) : ControllerBase
{
    /// <inheritdoc cref="AdministrationApiService.GetAppsAsync"/>
    [HttpGet(AdministrationRoutes.Apps, Name = "GetAdministeredApps")]
    [EndpointSummary("Lists the apps the signed-in administrator may manage")]
    public async Task<Ok<IReadOnlyList<AppSummaryResponse>>> GetAppsAsync(CancellationToken cancellationToken) =>
        TypedResults.Ok(await administrationApiSvc.GetAppsAsync(User, cancellationToken));

    /// <inheritdoc cref="AdministrationApiService.GetAgentsAsync"/>
    [HttpGet(AdministrationRoutes.Agents, Name = "GetAdministeredAgents")]
    [EndpointSummary("Gets one page of the app's agents, including inactive and never-activated agents")]
    public async Task<Ok<AgentSummaryPageResponse>> GetAgentsAsync(
        CancellationToken cancellationToken,
        [FromQuery, StringLength(200)] string? search = null,
        [FromQuery, Range(1, 10_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 25) =>
        TypedResults.Ok(await administrationApiSvc.GetAgentsAsync(search, page, pageSize, cancellationToken));

    /// <inheritdoc cref="AdministrationApiService.GetProviderCatalogue"/>
    [HttpGet(AdministrationRoutes.Providers, Name = "GetAdministeredProviders")]
    [EndpointSummary("Gets the app's secret-free provider registrations and definition policy")]
    public Ok<ProviderCatalogueResponse> GetProviders(string appId) =>
        TypedResults.Ok(administrationApiSvc.GetProviderCatalogue(appId));
}
