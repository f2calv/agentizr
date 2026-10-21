using Microsoft.Extensions.Hosting;

namespace CasCap.IntegrationTests;

/// <summary>Runs agentizr with the opt-in MCP endpoint enabled.</summary>
public sealed class McpAgentizrWebApplicationFactory : AgentizrWebApplicationFactory
{
    private const string McpEnabledVariable = "CasCap__McpConfig__Enabled";

    /// <inheritdoc/>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        //The host reads this flag before the test configuration sources are applied, so it travels as an environment variable.
        var previous = Environment.GetEnvironmentVariable(McpEnabledVariable);
        Environment.SetEnvironmentVariable(McpEnabledVariable, "true");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpEnabledVariable, previous);
        }
    }
}
