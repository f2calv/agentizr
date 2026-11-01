using CasCap.Common.Configuration.Management.Contracts.Models;
using CasCap.Common.Configuration.Runtime.Models;
using System.Security.Claims;

namespace CasCap.RuntimeConfiguration.Services;

/// <summary>Separates host-wide logging operators from tenant-only administration rights.</summary>
public sealed class RuntimeLoggingManagementService(
    IOptions<AdministrationConfig> administrationConfig,
    RuntimeConfigurationConfig settings,
    RuntimeConfigurationOptions runtimeOptions)
{
    /// <summary>Gets the immutable, non-secret destination served by this host.</summary>
    public RuntimeConfigurationDestination Destination =>
        new(runtimeOptions.Scope.Application, runtimeOptions.Scope.Environment, runtimeOptions.ContractId);

    /// <summary>Checks the already authenticated administration-cookie principal against the explicit grant.</summary>
    public bool IsOperator(ClaimsPrincipal principal)
    {
        if (!settings.ManagementEnabled || !administrationConfig.Value.Enabled
            || principal.Identity?.IsAuthenticated is not true)
            return false;
        var actor = principal.FindFirstValue(administrationConfig.Value.AdministratorClaimType);
        return !string.IsNullOrWhiteSpace(actor) && (settings.ManagementUseAppAdministratorAssignments
            ? administrationConfig.Value.AppAdministrators.Values.Any(ids => ids.Contains(actor, StringComparer.OrdinalIgnoreCase))
            : settings.ManagementOperators.Contains(actor, StringComparer.OrdinalIgnoreCase));
    }
}
