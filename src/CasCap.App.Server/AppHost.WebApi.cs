namespace CasCap;

/// <summary>Registers the HTTP API baseline.</summary>
public static partial class AppHost
{
    private static void AddWebApi(WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddControllers();
    }
}
