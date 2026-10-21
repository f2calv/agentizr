using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore;

namespace CasCap;

/// <summary>Registers and maps the opt-in MCP endpoint.</summary>
public static partial class AppHost
{
    private const string McpPath = "/mcp";

    private static bool AddMcp(WebApplicationBuilder builder, bool migrateOnly)
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

    private static void MapMcp(WebApplication app, bool mcpEnabled)
    {
        if (!mcpEnabled)
            return;

        // Runtime clients send no Origin; a browser always does, so reject it before any tool runs.
        app.MapGroup(McpPath)
            .AddEndpointFilter((context, next) =>
                context.HttpContext.Request.Headers.ContainsKey(HeaderNames.Origin)
                    ? ValueTask.FromResult<object?>(TypedResults.StatusCode(StatusCodes.Status403Forbidden))
                    : next(context))
            .MapMcp();
    }
}
