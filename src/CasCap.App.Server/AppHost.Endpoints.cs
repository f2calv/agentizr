namespace CasCap;

/// <summary>Maps the HTTP endpoint baseline.</summary>
public static partial class AppHost
{
    private static void MapEndpoints(WebApplication app, bool tenantAuthenticationEnabled)
    {
        app.UseExceptionHandler();
        if (tenantAuthenticationEnabled)
            app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "agentizr"
        }));
        var controllers = app.MapControllers();
        if (!tenantAuthenticationEnabled && app.Environment.IsDevelopment())
            controllers.AllowAnonymous();
        app.MapHealthChecks("/healthz");
    }
}
