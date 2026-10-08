using System.Security.Claims;

namespace CasCap.Services;

/// <summary>Resolves tenant identity from a validated authenticated principal.</summary>
internal sealed class AuthenticatedTenantContext(
    IHttpContextAccessor httpContextAccessor,
    IOptions<TenantAuthenticationConfig> authenticationConfig) : ITenantContext
{
    /// <inheritdoc/>
    public string TenantId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated is not true)
                throw new UnauthorizedAccessException("An authenticated tenant identity is required.");

            var callerId = principal.FindFirstValue(authenticationConfig.Value.CallerClaimType);
            if (string.IsNullOrWhiteSpace(callerId))
                throw new UnauthorizedAccessException("The authenticated identity has no caller claim.");

            foreach (var (tenantId, callerIds) in authenticationConfig.Value.TenantCallers)
            {
                if (callerIds.Contains(callerId, StringComparer.OrdinalIgnoreCase))
                    return tenantId;
            }

            throw new UnauthorizedAccessException("The authenticated caller is not assigned to a tenant.");
        }
    }
}
