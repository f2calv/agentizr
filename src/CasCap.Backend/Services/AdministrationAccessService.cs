using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace CasCap.Services;

/// <summary>Decides which apps a human administrator may manage from configuration-only assignments.</summary>
internal sealed class AdministrationAccessService(
    IOptions<AdministrationConfig> administrationConfig,
    IOptions<TenantAuthenticationConfig> tenantAuthenticationConfig,
    IOptions<AgentRuntimeConfig> runtimeConfig,
    IHostEnvironment hostEnvironment)
{
    /// <summary>
    /// Gets whether every configured app is open without sign-in, which happens only in Development with
    /// both administration and tenant authentication disabled.
    /// </summary>
    public bool IsOpenDevelopmentAccess =>
        !administrationConfig.Value.Enabled
        && !tenantAuthenticationConfig.Value.Enabled
        && hostEnvironment.IsDevelopment();

    /// <summary>Returns whether endpoint metadata selects the app-administrator policy.</summary>
    public static bool IsAdministrationEndpoint(EndpointMetadataCollection metadata) =>
        metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(data => data.Policy == AdministrationPolicies.AppAdministrator);

    /// <summary>Gets the known apps the principal may manage, ordered by identifier.</summary>
    public IReadOnlyList<string> GetAdministeredApps(ClaimsPrincipal principal) =>
        [.. GetKnownApps().Where(appId => CanAdminister(principal, appId)).Order(StringComparer.Ordinal)];

    /// <summary>Returns whether the principal may manage the app, which must also be known to the server.</summary>
    /// <remarks>
    /// An app is known only when the runtime defines it, through caller mappings or tenant configuration. An
    /// administrator assignment alone, such as a retired or misspelled app, never creates one.
    /// </remarks>
    public bool CanAdminister(ClaimsPrincipal principal, string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || !GetKnownApps().Contains(appId))
            return false;
        if (IsOpenDevelopmentAccess)
            return true;
        if (!administrationConfig.Value.Enabled || principal.Identity?.IsAuthenticated is not true)
            return false;

        var administratorId = principal.FindFirstValue(administrationConfig.Value.AdministratorClaimType);
        return !string.IsNullOrWhiteSpace(administratorId)
            && administrationConfig.Value.AppAdministrators.TryGetValue(appId, out var administratorIds)
            && administratorIds.Contains(administratorId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the caller application identifiers permitted to run the app's agents.</summary>
    public IReadOnlyList<string> GetCallerApplications(string appId) =>
        tenantAuthenticationConfig.Value.TenantCallers.TryGetValue(appId, out var callerIds)
            ? [.. callerIds.Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    private HashSet<string> GetKnownApps()
    {
        var apps = new HashSet<string>(runtimeConfig.Value.Tenants.Keys, StringComparer.Ordinal);
        apps.UnionWith(tenantAuthenticationConfig.Value.TenantCallers.Keys);
        if (!tenantAuthenticationConfig.Value.Enabled)
            apps.Add(runtimeConfig.Value.DefaultTenantId);
        return apps;
    }
}
