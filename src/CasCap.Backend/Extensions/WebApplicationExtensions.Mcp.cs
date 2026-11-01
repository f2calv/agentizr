using Microsoft.Net.Http.Headers;

namespace CasCap.Extensions;

public static partial class WebApplicationExtensions
{
    private const string McpPath = "/mcp";

    /// <summary>Maps the enabled MCP endpoint with its existing browser Origin guard.</summary>
    /// <param name="app">Built application.</param>
    /// <param name="mcpEnabled">Whether MCP registration is enabled.</param>
    public static void MapAgentRuntimeMcp(this WebApplication app, bool mcpEnabled)
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
