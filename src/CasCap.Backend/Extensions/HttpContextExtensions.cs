using CasCap.Client.Constants;

namespace CasCap.Extensions;

/// <summary>Projects application-owned frontend availability without exposing internal services.</summary>
public static class HttpContextExtensions
{
    /// <summary>Writes the setup page for navigation while retaining plain API and protocol 404s.</summary>
    /// <param name="context">Request context.</param>
    /// <returns>The response operation.</returns>
    public static Task WriteAgentRuntimeFrontendUnavailableAsync(this HttpContext context)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || request.Path.StartsWithSegments("/api")
            || request.Path.StartsWithSegments(AdministrationRoutes.Prefix)
            || request.Path.StartsWithSegments("/mcp"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        var page = context.RequestServices.GetRequiredService<AdministrationSetupService>().RenderFrontendUnavailablePage(request);
        return Results.Content(page, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
    }
}
