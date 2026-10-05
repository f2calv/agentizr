namespace CasCap;

/// <summary>Maps the HTTP endpoint baseline.</summary>
public static partial class AppHost
{
    private static void MapEndpoints(WebApplication app, bool tenantAuthenticationEnabled)
    {
        app.UseExceptionHandler();
        if (tenantAuthenticationEnabled)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "agentizr"
        }));
        if (tenantAuthenticationEnabled || app.Environment.IsDevelopment())
            app.MapAgentRuntime(tenantAuthenticationEnabled);
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
