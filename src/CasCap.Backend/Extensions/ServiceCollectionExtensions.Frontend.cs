namespace CasCap.Extensions;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registers setup-page projection without registering the browser shell.</summary>
    /// <param name="services">Application services.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddAgentRuntimeFrontendSetup(this IServiceCollection services)
    {
        services.AddSingleton(provider => new AdministrationSetupService(
            provider.GetRequiredService<IOptions<AdministrationConfig>>(),
            provider.GetRequiredService<IOptions<TenantAuthenticationConfig>>(),
            provider.GetRequiredService<IHostEnvironment>()));
        return services;
    }
}
