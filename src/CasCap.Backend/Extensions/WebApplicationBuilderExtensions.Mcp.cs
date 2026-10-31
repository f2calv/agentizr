using ModelContextProtocol.AspNetCore;

namespace CasCap.Extensions;

/// <summary>Registers and maps the opt-in MCP endpoint.</summary>
public static partial class WebApplicationBuilderExtensions
{
    /// <summary>Registers the opt-in read-only MCP endpoint for the runtime role.</summary>
    /// <param name="builder">Application builder.</param>
    /// <param name="migrateOnly">Whether the host is running only database migrations.</param>
    /// <returns>Whether MCP is enabled.</returns>
    public static bool AddAgentRuntimeMcp(this WebApplicationBuilder builder, bool migrateOnly)
    {
        var mcpEnabled = !migrateOnly
            && (builder.Configuration.GetSection(McpConfig.ConfigurationSectionName).Get<McpConfig>()?.Enabled ?? false);
        if (mcpEnabled)
        {
            builder.Services.AddMcpServer()
                .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithTools<AgentizrMcpQueryService>();
        }
        return mcpEnabled;
    }

}
