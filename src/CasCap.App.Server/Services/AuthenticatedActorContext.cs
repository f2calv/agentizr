using System.Security.Claims;

namespace CasCap.Services;

/// <summary>Resolves control-plane actor identity from a validated authenticated principal.</summary>
internal sealed class AuthenticatedActorContext(IHttpContextAccessor httpContextAccessor) : IActorContext
{
    /// <inheritdoc/>
    public string ActorId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated is not true)
                throw new UnauthorizedAccessException("An authenticated control-plane identity is required.");
            var actorId = principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("client_id")
                ?? principal.FindFirstValue("azp")
                ?? principal.Identity.Name;
            return string.IsNullOrWhiteSpace(actorId)
                ? throw new UnauthorizedAccessException("The authenticated identity has no stable actor claim.")
                : actorId;
        }
    }
}
