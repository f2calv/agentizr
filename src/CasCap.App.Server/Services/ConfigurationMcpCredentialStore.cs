namespace CasCap.Services;

/// <summary>Resolves tenant remote MCP credentials from the private configuration-provider chain.</summary>
internal sealed class ConfigurationMcpCredentialStore(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    ITenantContext tenantContext) : IMcpCredentialStore
{
    /// <inheritdoc/>
    public ValueTask<string?> GetAuthorizationHeaderAsync(
        string credentialName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialName);
        var authorization = runtimeConfig.Value.Tenants.TryGetValue(tenantContext.TenantId, out var tenant)
            && tenant.McpCredentials.TryGetValue(credentialName, out var credential)
                ? $"{credential.Scheme} {credential.Parameter}"
                : null;
        return ValueTask.FromResult(authorization);
    }
}