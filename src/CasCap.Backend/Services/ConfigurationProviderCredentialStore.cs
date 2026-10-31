namespace CasCap.Services;

/// <summary>Resolves tenant provider credentials from the final configuration provider chain.</summary>
internal sealed class ConfigurationProviderCredentialStore(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext,
    RuntimeEntraCredential runtimeCredential) : IProviderCredentialStore
{
    /// <inheritdoc/>
    public ValueTask<string?> GetApiKeyAsync(string providerName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            && tenant.Providers.TryGetValue(providerName, out var provider)
                ? provider.ApiKey
                : null;

        return ValueTask.FromResult(apiKey);
    }

    /// <inheritdoc/>
    public Azure.Core.TokenCredential? GetTokenCredential(string providerName) =>
        runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            && tenant.EntraIdProviders.Contains(providerName)
                ? runtimeCredential.Credential
                : null;
}
