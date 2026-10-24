using CasCap.Components;

namespace CasCap;

/// <summary>Registers and maps the browser administration frontend, a WebAssembly client served by this host.</summary>
public static partial class AppHost
{
    private static void AddFrontend(WebApplicationBuilder builder) =>
        builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

    // The frontend is served only where administrators can use it: with sign-in enabled, or in Development.
    private static void MapFrontend(WebApplication app, bool administrationEnabled)
    {
        if (!administrationEnabled && !app.Environment.IsDevelopment())
            return;
        if (app.Environment.IsDevelopment())
            app.UseWebAssemblyDebugging();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Client.Routes).Assembly);
    }
}
