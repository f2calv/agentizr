using CasCap.Client.Constants;

namespace CasCap.Services;

/// <summary>
/// Selects the route's app as the tenant on administration endpoints, whose policy has already authorized it,
/// and otherwise defers to the caller-derived tenant.
/// </summary>
internal sealed class AdministrationTenantContext(
    IHttpContextAccessor httpContextAccessor,
    ITenantContext callerTenantContext) : ITenantContext
{
    /// <inheritdoc/>
    public string TenantId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.GetEndpoint() is not { } endpoint
                || !AdministrationAccessService.IsAdministrationEndpoint(endpoint.Metadata))
                return callerTenantContext.TenantId;

            return httpContext.GetRouteValue(AdministrationRoutes.AppIdParameter) as string
                ?? throw new InvalidOperationException("This administration endpoint does not select an app.");
        }
    }
}
