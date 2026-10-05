namespace CasCap.Services;

/// <summary>Supplies the configured default tenant until authenticated claims become authoritative.</summary>
internal sealed class ConfiguredTenantContext(IOptions<AgentRuntimeConfig> runtimeConfig) : ITenantContext
{
    /// <inheritdoc/>
    public string TenantId => runtimeConfig.Value.DefaultTenantId;
}
