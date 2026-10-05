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
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
