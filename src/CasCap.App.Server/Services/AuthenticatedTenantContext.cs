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

            var tenantId = principal.FindFirstValue(authenticationConfig.Value.TenantClaimType);
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new UnauthorizedAccessException("The authenticated identity has no tenant claim.");

            return tenantId;
        }
    }
}
