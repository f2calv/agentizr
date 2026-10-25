using CasCap.Client.Constants;
using CasCap.Components;

namespace CasCap;

/// <summary>Registers and maps the browser administration frontend, a WebAssembly client served by this host.</summary>
public static partial class AppHost
{
    private static void AddFrontend(WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
        builder.Services.AddSingleton(services => new AdministrationSetupService(
            services.GetRequiredService<IOptions<AdministrationConfig>>(),
            services.GetRequiredService<IOptions<TenantAuthenticationConfig>>(),
            services.GetRequiredService<IHostEnvironment>()));
    }

    // Served only where administrators can use it: with sign-in enabled, or in open Development. Elsewhere,
    // page navigations get a setup page explaining the missing configuration.
    private static void MapFrontend(WebApplication app, bool administrationEnabled)
    {
        if (!administrationEnabled && !app.Services.GetRequiredService<AdministrationAccessService>().IsOpenDevelopmentAccess)
        {
            app.MapFallback(WriteFrontendUnavailableAsync);
            return;
        }
        if (app.Environment.IsDevelopment())
            app.UseWebAssemblyDebugging();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Client.Routes).Assembly);
    }

    private static Task WriteFrontendUnavailableAsync(HttpContext context)
    {
        var request = context.Request;
        // API and protocol clients keep a plain 404; only page navigations get the explanation.
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
