using CasCap.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CasCap;

/// <summary>Registers the HTTP API baseline.</summary>
public static partial class AppHost
{
    private static bool AddWebApi(WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddControllers();
        builder.Services.AddValidation();

        var authenticationConfig = builder.Configuration
            .GetSection(TenantAuthenticationConfig.ConfigurationSectionName)
            .Get<TenantAuthenticationConfig>() ?? new TenantAuthenticationConfig();
        builder.Services.AddOptionsWithValidateOnStart<TenantAuthenticationConfig>()
            .BindConfiguration(TenantAuthenticationConfig.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .Validate(
                config => !config.Enabled
                    || AgentRuntimeAuthorization.HasValidTenantCallers(config.TenantCallers),
                "Tenant caller mappings must contain unique, non-empty caller identifiers for non-empty tenants.");

        if (!authenticationConfig.Enabled)
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("JWT tenant authentication is required outside Development.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(authenticationConfig.Authority)
            || string.IsNullOrWhiteSpace(authenticationConfig.Audience))
            throw new InvalidOperationException("JWT tenant authentication requires Authority and Audience.");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authenticationConfig.Authority;
                options.Audience = authenticationConfig.Audience;
                options.RequireHttpsMetadata = authenticationConfig.HttpsMetadataRequired;
                options.MapInboundClaims = false;
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AgentRuntimePolicies.Execute, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User,
                    AgentRuntimePermissions.Execute));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User,
                    AgentRuntimePermissions.DefinitionRead));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionPublish, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User,
                    AgentRuntimePermissions.DefinitionPublish));
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasActorIdentifier(context.User));
            });
            options.AddPolicy(AgentRuntimePolicies.DefinitionActivate, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasPermission(
                    context.User,
                    AgentRuntimePermissions.DefinitionActivate));
                policy.RequireAssertion(context => AgentRuntimeAuthorization.HasActorIdentifier(context.User));
            });
        });
        return true;
    }

}
