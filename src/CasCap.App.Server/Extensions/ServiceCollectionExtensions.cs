using CasCap.Constants;
using Microsoft.AspNetCore.Authorization;

namespace CasCap.Extensions;

/// <summary>Registers the controller API baseline, tenant permission policies and app administration.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds controller validation, session-conflict handling, separate runtime/control-plane policies and the
    /// app-administrator policy.
    /// </summary>
    public static IServiceCollection AddAgentRuntimeApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<AgentSessionConflictHandler>();
        services.AddHealthChecks();
        services.AddControllers(options =>
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
        services.AddScoped<AgentRuntimeApiService>();
        services.AddScoped(services => new AgentDefinitionApiService(
            services.GetRequiredService<AgentDefinitionAdministrationService>()));
        services.AddSingleton<AdministrationAccessService>();
        services.AddSingleton<IAuthorizationHandler, AppAdministratorAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdministrationAuthorizationResultHandler>();
        services.AddScoped(services => new AdministrationApiService(
            services.GetRequiredService<IOptions<AgentDefinitionPolicyConfig>>(),
            services.GetRequiredService<IOptions<AgentRuntimeConfig>>(),
            services.GetRequiredService<IOptions<DatabaseConfig>>(),
            services.GetRequiredService<AdministrationAccessService>(),
            services.GetRequiredService<AgentDefinitionAdministrationService>()));
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AgentRuntimePolicies.Execute, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User, AgentRuntimePermissions.Execute));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User, AgentRuntimePermissions.DefinitionRead));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionPublish, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User, AgentRuntimePermissions.DefinitionPublish));
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasActorIdentifier(context.User));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionActivate, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User, AgentRuntimePermissions.DefinitionActivate));
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasActorIdentifier(context.User));
            });
        });
        services.AddOptions<AuthorizationOptions>().Configure<IOptions<AdministrationConfig>>((options, administrationConfig) =>
        {
            options.AddPolicy(AdministrationPolicies.AppAdministrator, policy =>
            {
                // Only the administration cookie counts; machine bearer tokens never grant administration.
                if (administrationConfig.Value.Enabled)
                    policy.AddAuthenticationSchemes(AdministrationAuthenticationSchemes.Cookie);
                policy.AddRequirements(new AppAdministratorRequirement());
            });
            options.AddPolicy(AdministrationPolicies.AuthenticatedDocumentReviewer, policy =>
            {
                if (administrationConfig.Value.Enabled)
                    policy.AddAuthenticationSchemes(AdministrationAuthenticationSchemes.Cookie);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(_ => administrationConfig.Value.Enabled);
            });
        });
        return services;
    }
}
