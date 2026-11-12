namespace CasCap.Services;

/// <summary>A tenant context fixed at construction, for work that outlives or runs outside the authenticating request.</summary>
internal sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    /// <inheritdoc/>
    public string TenantId => tenantId;
}
