using CasCap.Models;

namespace CasCap.IntegrationTests.Local;

/// <summary>Environment-provided settings for a live Agent Runtime smoke test.</summary>
public sealed record LiveAgentRuntimeTestSettings
{
    /// <summary>Gets the live runtime base address.</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>Gets the tenant-local agent name.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets optional certificate authentication.</summary>
    public AgentRuntimeAzureAuthConfig? Authentication { get; init; }

    /// <summary>Gets the HTTP timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Loads local settings and optional certificate content from environment-owned paths.</summary>
    public static bool TryLoad(out LiveAgentRuntimeTestSettings? settings)
    {
        var baseAddressValue = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_BASE_ADDRESS");
        var agentName = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_AGENT_NAME");
        if (!Uri.TryCreate(baseAddressValue, UriKind.Absolute, out var baseAddress)
            || string.IsNullOrWhiteSpace(agentName))
        {
            settings = null;
            return false;
        }

        AgentRuntimeAzureAuthConfig? authentication = null;
        var scope = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_SCOPE");
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var tenantIdValue = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_TENANT_ID");
            var clientIdValue = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_CLIENT_ID");
            var certificatePath = Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_CERTIFICATE_PATH");
            if (!Guid.TryParse(tenantIdValue, out var tenantId)
                || !Guid.TryParse(clientIdValue, out var clientId)
                || string.IsNullOrWhiteSpace(certificatePath)
                || !File.Exists(certificatePath))
            {
                settings = null;
                return false;
            }

            authentication = new AgentRuntimeAzureAuthConfig
            {
                Enabled = true,
                TenantId = tenantId,
                ClientId = clientId,
                Certificate = File.ReadAllText(certificatePath),
                Scope = scope,
            };
        }

        var timeout = int.TryParse(
            Environment.GetEnvironmentVariable("CASCAP_AGENTRUNTIME_LIVE_TIMEOUT_SECONDS"),
            out var timeoutSeconds)
            && timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeSpan.FromMinutes(5);

        settings = new LiveAgentRuntimeTestSettings
        {
            BaseAddress = baseAddress,
            AgentName = agentName,
            Authentication = authentication,
            Timeout = timeout,
        };
        return true;
    }
}