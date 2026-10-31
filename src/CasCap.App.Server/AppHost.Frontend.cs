using CasCap.Components;
using CasCap.Extensions;

namespace CasCap;

/// <summary>Registers and maps the browser administration frontend, a WebAssembly client served by this host.</summary>
public static partial class AppHost
{
    private static void AddFrontend(WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
        builder.Services.AddAgentRuntimeFrontendSetup();
    }

    // Served only where administrators can use it: with sign-in enabled, or in open Development. Elsewhere,
    // page navigations get a setup page explaining the missing configuration.
    private static void MapFrontend(WebApplication app, bool administrationEnabled)
    {
        if (!app.HasAgentRuntimeFrontendAccess(administrationEnabled))
        {
            app.MapFallback(context => context.WriteAgentRuntimeFrontendUnavailableAsync());
            return;
        }
        if (app.Environment.IsDevelopment())
            app.UseWebAssemblyDebugging();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Client.Routes).Assembly);
    }

}
