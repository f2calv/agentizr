namespace CasCap.Extensions;

public static partial class WebApplicationExtensions
{
    /// <summary>Checks whether browser administration can be served for this startup composition.</summary>
    /// <param name="app">Built application.</param>
    /// <param name="administrationEnabled">Whether administration authentication is enabled.</param>
    /// <returns>Whether sign-in or open Development access permits the frontend.</returns>
    public static bool HasAgentRuntimeFrontendAccess(this WebApplication app, bool administrationEnabled) =>
        administrationEnabled || app.Services.GetRequiredService<AdministrationAccessService>().IsOpenDevelopmentAccess;
}
