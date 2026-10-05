namespace CasCap;

/// <summary>Maps the HTTP endpoint baseline.</summary>
public static partial class AppHost
{
    private static void MapEndpoints(WebApplication app)
    {
        app.UseExceptionHandler();

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "agentizr"
        }));
        if (app.Environment.IsDevelopment())
            app.MapAgentRuntime();
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
