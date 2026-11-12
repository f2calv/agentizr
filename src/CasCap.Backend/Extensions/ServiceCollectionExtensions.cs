using CasCap.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace CasCap.Extensions;

/// <summary>Registers the controller API baseline, tenant permission policies and app administration.</summary>
public static partial class ServiceCollectionExtensions
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
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddApplicationPart(typeof(Controllers.AgentRuntimeController).Assembly);
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
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>().Configure<IOptions<TenantAuthenticationConfig>>((options, tenantAuthenticationConfig) =>
        {
            // Machine policies authenticate bearer tokens only, never the administration cookie, so a browser's ambient
            // credential cannot reach them and they need no antiforgery token. Without tenant authentication no bearer
            // scheme exists and Development opens these endpoints anonymously instead.
            void AddMachinePolicy(string name, string permission, bool requireActor = false) =>
                options.AddPolicy(name, policy =>
                {
                    if (tenantAuthenticationConfig.Value.Enabled)
                        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(context.User, permission));
                    if (requireActor)
                        policy.RequireAssertion(context => AgentRuntimeAuthorization.HasActorIdentifier(context.User));
                });

            AddMachinePolicy(AgentRuntimePolicies.Execute, AgentRuntimePermissions.Execute);
            AddMachinePolicy(AgentRuntimePolicies.DefinitionRead, AgentRuntimePermissions.DefinitionRead);
            AddMachinePolicy(AgentRuntimePolicies.DefinitionPublish, AgentRuntimePermissions.DefinitionPublish, requireActor: true);
            AddMachinePolicy(AgentRuntimePolicies.DefinitionActivate, AgentRuntimePermissions.DefinitionActivate, requireActor: true);
            AddMachinePolicy(AgentRuntimePolicies.Realtime, AgentRuntimePermissions.Realtime);
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
