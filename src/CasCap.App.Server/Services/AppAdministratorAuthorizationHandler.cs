using CasCap.Client.Constants;
using Microsoft.AspNetCore.Authorization;

namespace CasCap.Services;

/// <summary>Authorizes administrators against the route-selected app, or any assigned app when none is selected.</summary>
internal sealed class AppAdministratorAuthorizationHandler(AdministrationAccessService accessSvc)
    : AuthorizationHandler<AppAdministratorRequirement>
{
    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppAdministratorRequirement requirement)
    {
        var appId = (context.Resource as HttpContext)?.GetRouteValue(AdministrationRoutes.AppIdParameter) as string;
        var allowed = appId is null
            ? accessSvc.GetAdministeredApps(context.User).Count > 0
            : accessSvc.CanAdminister(context.User, appId);
        if (allowed)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
