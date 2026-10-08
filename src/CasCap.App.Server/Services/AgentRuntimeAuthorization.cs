using System.Security.Claims;

namespace CasCap.Services;

/// <summary>Evaluates validated caller claims and tenant-mapping configuration.</summary>
internal static class AgentRuntimeAuthorization
{
    /// <summary>Returns whether a principal carries the required delegated scope or application role.</summary>
    public static bool HasPermission(ClaimsPrincipal principal, string requiredPermission) =>
        principal.FindAll("roles")
            .Concat(principal.FindAll("scope"))
            .Concat(principal.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredPermission, StringComparer.Ordinal);

    /// <summary>Returns whether a principal carries a stable actor identifier.</summary>
    public static bool HasActorIdentifier(ClaimsPrincipal principal) =>
        principal.Claims.Any(claim => claim.Type is "sub" or "client_id" or "azp"
            or ClaimTypes.NameIdentifier or ClaimTypes.Name);

    /// <summary>Returns whether every configured caller belongs to exactly one non-empty tenant.</summary>
    public static bool HasValidTenantCallers(IReadOnlyDictionary<string, string[]> mappings)
    {
        if (mappings.Count == 0 || mappings.Any(mapping => string.IsNullOrWhiteSpace(mapping.Key)
            || mapping.Value.Length == 0
            || mapping.Value.Any(string.IsNullOrWhiteSpace)))
            return false;

        return mappings.Values
            .SelectMany(callerIds => callerIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == mappings.Values.Sum(callerIds => callerIds.Length);
    }
}