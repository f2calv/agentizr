using System.Text;

namespace CasCap.Services;

/// <summary>Resolves tenant remote MCP credentials from the private configuration-provider chain.</summary>
internal sealed class ConfigurationMcpCredentialStore(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    IOptions<ApiAuthConfig> apiAuthConfig,
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
                ? FormatAuthorization(credential)
                : null;
        return ValueTask.FromResult(authorization);
    }

    private string? FormatAuthorization(McpCredentialConfig credential)
    {
        if (!string.IsNullOrWhiteSpace(credential.Parameter))
            return $"{credential.Scheme} {credential.Parameter}";

        var shared = apiAuthConfig.Value;
        return credential.IsBasic
            && !string.IsNullOrWhiteSpace(shared.Username)
            && !string.IsNullOrWhiteSpace(shared.Password)
                ? $"{credential.Scheme} {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{shared.Username}:{shared.Password}"))}"
                : null;
    }
}