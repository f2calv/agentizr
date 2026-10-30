namespace CasCap;

/// <summary>Maps the HTTP endpoint baseline.</summary>
public static partial class AppHost
{
    private static void MapEndpoints(WebApplication app, bool tenantAuthenticationEnabled, bool administrationEnabled)
    {
        app.UseExceptionHandler();
        if (tenantAuthenticationEnabled || administrationEnabled)
            app.UseAuthentication();
        app.UseAuthorization();
        // Validates Razor component endpoints only; administration controllers use their own antiforgery filter.
        app.UseAntiforgery();

        MapStatusEndpoint(app);
        var controllers = app.MapControllers();
        if (!tenantAuthenticationEnabled && app.Environment.IsDevelopment())
        {
            // Administration keeps its own policy, which opens it only when administration is also disabled.
            controllers.Add(endpoint =>
            {
                if (!AdministrationAccessService.IsAdministrationEndpoint(new EndpointMetadataCollection(endpoint.Metadata)))
                    endpoint.Metadata.Add(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());
            });
        }
        app.MapHealthChecks("/healthz");
    }

    /// <summary>Maps baseline status with payload-free logging evidence only for an enabled Debug soak.</summary>
    internal static void MapStatusEndpoint(WebApplication app)
    {
#if DEBUG
        var probe = app.Services.GetService<CasCap.RuntimeConfiguration.Services.RuntimeLoggingProbe>();
#endif
        app.MapGet("/status", () =>
        {
#if DEBUG
            if (probe is not null)
                return Results.Ok(new
                {
                    Service = "agentizr",
                    RuntimeLogging = probe.ReadStatus(),
                });
#endif
            return Results.Ok(new
            {
                Service = "agentizr",
            });
        });
    }
}
