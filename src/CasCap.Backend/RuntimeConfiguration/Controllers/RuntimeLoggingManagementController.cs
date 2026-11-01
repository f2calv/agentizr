using CasCap.Client.Constants;
using CasCap.Common.Configuration.Management.Contracts.Constants;
using CasCap.Common.Configuration.Management.Contracts.Models;
using CasCap.RuntimeConfiguration.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.RuntimeConfiguration.Controllers;

/// <summary>Describes the fixed logging destination only to configured host-wide operators.</summary>
[ApiController]
[Authorize(Policy = RuntimeConfigurationManagementPolicies.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RuntimeLoggingManagementController(RuntimeLoggingManagementService managementSvc) : ControllerBase
{
    /// <inheritdoc cref="RuntimeLoggingManagementService.Destination"/>
    [HttpGet(RuntimeLoggingRoutes.Destination)]
    public Ok<RuntimeConfigurationDestination> GetDestination() => TypedResults.Ok(managementSvc.Destination);
}
